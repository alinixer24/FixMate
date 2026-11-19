using System.Globalization;
using FixMate.Core.Interfaces;
using FixMate.Core.Models;
using Microsoft.Data.Sqlite;

namespace FixMate.Infrastructure.Storage;

public sealed class SqliteMaintenanceHistoryStore : IMaintenanceHistoryStore
{
    private const int HistoryLimit = 500;
    private readonly string _connectionString;

    public SqliteMaintenanceHistoryStore()
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

    public async Task<IReadOnlyList<MaintenanceHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Operation, Outcome, Details, CompletedAt FROM MaintenanceOperationHistory ORDER BY CompletedAt DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", HistoryLimit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var entries = new List<MaintenanceHistoryEntry>();
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new MaintenanceHistoryEntry(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return entries;
    }

    public async Task RecordAsync(MaintenanceHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (string.IsNullOrWhiteSpace(entry.Operation))
        {
            throw new ArgumentException("A maintenance operation name is required.", nameof(entry));
        }

        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var transaction = connection.BeginTransaction();
        await using (var insertCommand = connection.CreateCommand())
        {
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = "INSERT INTO MaintenanceOperationHistory (Id, Operation, Outcome, Details, CompletedAt) VALUES ($id, $operation, $outcome, $details, $completedAt);";
            insertCommand.Parameters.AddWithValue("$id", entry.Id.ToString("D"));
            insertCommand.Parameters.AddWithValue("$operation", entry.Operation);
            insertCommand.Parameters.AddWithValue("$outcome", entry.Outcome);
            insertCommand.Parameters.AddWithValue("$details", entry.Details);
            insertCommand.Parameters.AddWithValue("$completedAt", entry.CompletedAt.ToString("O", CultureInfo.InvariantCulture));
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var trimCommand = connection.CreateCommand())
        {
            trimCommand.Transaction = transaction;
            trimCommand.CommandText = "DELETE FROM MaintenanceOperationHistory WHERE Id NOT IN (SELECT Id FROM MaintenanceOperationHistory ORDER BY CompletedAt DESC, rowid DESC LIMIT $limit);";
            trimCommand.Parameters.AddWithValue("$limit", HistoryLimit);
            await trimCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
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
            CREATE TABLE IF NOT EXISTS MaintenanceOperationHistory (
                Id TEXT PRIMARY KEY NOT NULL,
                Operation TEXT NOT NULL,
                Outcome TEXT NOT NULL,
                Details TEXT NOT NULL,
                CompletedAt TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
