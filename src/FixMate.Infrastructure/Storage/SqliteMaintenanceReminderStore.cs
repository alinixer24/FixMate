using System.Globalization;
using FixMate.Core.Interfaces;
using FixMate.Core.Models;
using Microsoft.Data.Sqlite;

namespace FixMate.Infrastructure.Storage;

public sealed class SqliteMaintenanceReminderStore : IMaintenanceReminderStore
{
    private readonly string _connectionString;

    public SqliteMaintenanceReminderStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            throw new InvalidOperationException("The current user's local application data folder is unavailable.");
        }

        var directory = Path.Combine(appData, "FixMate");
        Directory.CreateDirectory(directory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "fixmate.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task<IReadOnlyList<MaintenanceReminder>> GetRemindersAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, DueAt, RepeatInterval, IsEnabled, Notify FROM MaintenanceReminders ORDER BY DueAt;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var reminders = new List<MaintenanceReminder>();
        while (await reader.ReadAsync(cancellationToken))
        {
            reminders.Add(ReadReminder(reader));
        }

        return reminders;
    }

    public async Task<IReadOnlyList<MaintenanceReminder>> GetDueRemindersAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, DueAt, RepeatInterval, IsEnabled, Notify FROM MaintenanceReminders WHERE IsEnabled = 1 AND DueAt <= $now ORDER BY DueAt;";
        command.Parameters.AddWithValue("$now", now.ToString("O", CultureInfo.InvariantCulture));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var reminders = new List<MaintenanceReminder>();
        while (await reader.ReadAsync(cancellationToken))
        {
            reminders.Add(ReadReminder(reader));
        }

        return reminders;
    }

    public async Task SaveReminderAsync(MaintenanceReminder reminder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reminder);
        if (string.IsNullOrWhiteSpace(reminder.Name))
        {
            throw new ArgumentException("A reminder name is required.", nameof(reminder));
        }

        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO MaintenanceReminders (Id, Name, DueAt, RepeatInterval, IsEnabled, Notify)
            VALUES ($id, $name, $dueAt, $repeatInterval, $isEnabled, $notify)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                DueAt = excluded.DueAt,
                RepeatInterval = excluded.RepeatInterval,
                IsEnabled = excluded.IsEnabled,
                Notify = excluded.Notify;
            """;
        AddReminderParameters(command, reminder);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CompleteReminderAsync(MaintenanceReminder reminder, DateTimeOffset completedAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reminder);
        var nextDueAt = GetNextDueAt(reminder, completedAt);
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var transaction = connection.BeginTransaction();

        await using (var historyCommand = connection.CreateCommand())
        {
            historyCommand.Transaction = transaction;
            historyCommand.CommandText = "INSERT INTO MaintenanceReminderHistory (ReminderId, ReminderName, CompletedAt, NextDueAt) VALUES ($id, $name, $completedAt, $nextDueAt);";
            historyCommand.Parameters.AddWithValue("$id", reminder.Id.ToString("D"));
            historyCommand.Parameters.AddWithValue("$name", reminder.Name);
            historyCommand.Parameters.AddWithValue("$completedAt", completedAt.ToString("O", CultureInfo.InvariantCulture));
            historyCommand.Parameters.AddWithValue("$nextDueAt", nextDueAt?.ToString("O", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
            await historyCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var updateCommand = connection.CreateCommand())
        {
            updateCommand.Transaction = transaction;
            updateCommand.CommandText = "UPDATE MaintenanceReminders SET DueAt = $dueAt, IsEnabled = $enabled WHERE Id = $id;";
            updateCommand.Parameters.AddWithValue("$dueAt", nextDueAt?.ToString("O", CultureInfo.InvariantCulture) ?? reminder.DueAt.ToString("O", CultureInfo.InvariantCulture));
            updateCommand.Parameters.AddWithValue("$enabled", nextDueAt.HasValue ? 1 : 0);
            updateCommand.Parameters.AddWithValue("$id", reminder.Id.ToString("D"));
            await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReminderHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ReminderId, ReminderName, CompletedAt, NextDueAt FROM MaintenanceReminderHistory ORDER BY CompletedAt DESC LIMIT 200;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var history = new List<ReminderHistoryEntry>();
        while (await reader.ReadAsync(cancellationToken))
        {
            history.Add(new ReminderHistoryEntry(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
                reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture)));
        }

        return history;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS MaintenanceReminders (
                Id TEXT PRIMARY KEY NOT NULL,
                Name TEXT NOT NULL,
                DueAt TEXT NOT NULL,
                RepeatInterval TEXT NOT NULL,
                IsEnabled INTEGER NOT NULL,
                Notify INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS MaintenanceReminderHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ReminderId TEXT NOT NULL,
                ReminderName TEXT NOT NULL,
                CompletedAt TEXT NOT NULL,
                NextDueAt TEXT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static MaintenanceReminder ReadReminder(SqliteDataReader reader)
    {
        return new MaintenanceReminder(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
            reader.GetString(3),
            reader.GetInt64(4) != 0,
            reader.GetInt64(5) != 0);
    }

    private static void AddReminderParameters(SqliteCommand command, MaintenanceReminder reminder)
    {
        command.Parameters.AddWithValue("$id", reminder.Id.ToString("D"));
        command.Parameters.AddWithValue("$name", reminder.Name);
        command.Parameters.AddWithValue("$dueAt", reminder.DueAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$repeatInterval", reminder.RepeatInterval);
        command.Parameters.AddWithValue("$isEnabled", reminder.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$notify", reminder.Notify ? 1 : 0);
    }

    private static DateTimeOffset? GetNextDueAt(MaintenanceReminder reminder, DateTimeOffset completedAt)
    {
        return reminder.RepeatInterval switch
        {
            "Daily" => Advance(reminder.DueAt, completedAt, dueAt => dueAt.AddDays(1)),
            "Weekly" => Advance(reminder.DueAt, completedAt, dueAt => dueAt.AddDays(7)),
            "Monthly" => Advance(reminder.DueAt, completedAt, dueAt => dueAt.AddMonths(1)),
            _ => null
        };
    }

    private static DateTimeOffset Advance(DateTimeOffset dueAt, DateTimeOffset completedAt, Func<DateTimeOffset, DateTimeOffset> advance)
    {
        var next = advance(dueAt);
        while (next <= completedAt)
        {
            next = advance(next);
        }

        return next;
    }
}
