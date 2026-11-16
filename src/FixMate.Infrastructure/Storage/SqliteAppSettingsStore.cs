using FixMate.Core.Interfaces;
using FixMate.Core.Models;
using Microsoft.Data.Sqlite;

namespace FixMate.Infrastructure.Storage;

public sealed class SqliteAppSettingsStore : IAppSettingsStore
{
    private readonly string _connectionString;

    public SqliteAppSettingsStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            throw new InvalidOperationException("The current user's local application data folder is unavailable.");
        }

        var databaseDirectory = Path.Combine(appData, "FixMate");
        Directory.CreateDirectory(databaseDirectory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(databaseDirectory, "fixmate.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var createCommand = connection.CreateCommand();
        createCommand.CommandText = """
            CREATE TABLE IF NOT EXISTS AppSettings (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                MonitoringRefreshIntervalSeconds INTEGER NOT NULL CHECK (MonitoringRefreshIntervalSeconds IN (1, 2, 5, 10))
            );
            """;
        await createCommand.ExecuteNonQueryAsync(cancellationToken);

        await using var readCommand = connection.CreateCommand();
        readCommand.CommandText = "SELECT MonitoringRefreshIntervalSeconds FROM AppSettings WHERE Id = 1;";
        var result = await readCommand.ExecuteScalarAsync(cancellationToken);
        if (result is null or DBNull)
        {
            return new AppSettings();
        }

        return new AppSettings { MonitoringRefreshIntervalSeconds = Convert.ToInt32(result) };
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS AppSettings (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                MonitoringRefreshIntervalSeconds INTEGER NOT NULL CHECK (MonitoringRefreshIntervalSeconds IN (1, 2, 5, 10))
            );
            INSERT INTO AppSettings (Id, MonitoringRefreshIntervalSeconds)
            VALUES (1, $interval)
            ON CONFLICT(Id) DO UPDATE SET
                MonitoringRefreshIntervalSeconds = excluded.MonitoringRefreshIntervalSeconds;
            """;
        command.Parameters.AddWithValue("$interval", settings.MonitoringRefreshIntervalSeconds);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
