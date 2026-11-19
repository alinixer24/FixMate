using FixMate.Core.Interfaces;
using Microsoft.Data.Sqlite;

namespace FixMate.Infrastructure.Storage;

public sealed class LocalApplicationDataService : IApplicationDataService
{
    private readonly string _applicationDirectory;
    private readonly string _connectionString;

    public LocalApplicationDataService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            throw new InvalidOperationException("The current user's local application data folder is unavailable.");
        }

        _applicationDirectory = Path.Combine(appData, "FixMate");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_applicationDirectory, "fixmate.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task ClearUserDataAsync(CancellationToken cancellationToken = default)
    {
        DeleteFilesInDirectory("Reports");
        DeleteFilesInDirectory("Logs");

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        foreach (var table in new[] { "MaintenanceReminderHistory", "MaintenanceReminders", "MaintenanceOperationHistory", "NetworkDiagnosticHistory" })
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT EXISTS (SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $table);";
            command.Parameters.AddWithValue("$table", table);
            if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) == 0)
            {
                continue;
            }

            command.Parameters.Clear();
            command.CommandText = $"DELETE FROM [{table}];";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private void DeleteFilesInDirectory(string directoryName)
    {
        var directory = Path.Combine(_applicationDirectory, directoryName);
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            File.Delete(file);
        }
    }
}
