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
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var readCommand = connection.CreateCommand();
        readCommand.CommandText = "SELECT MonitoringRefreshIntervalSeconds, ThemeMode, AccentColor, ReduceAnimations, LaunchAtStartup, StartMinimized, GraphHistoryDurationMinutes, CompactLayout, NotificationsEnabled, SidebarCollapsed, CpuWarningThresholdPercent, MemoryWarningThresholdPercent FROM AppSettings WHERE Id = 1;";
        await using var reader = await readCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new AppSettings();
        }

        return new AppSettings
        {
            MonitoringRefreshIntervalSeconds = reader.GetInt32(0),
            ThemeMode = reader.GetString(1),
            AccentColor = reader.GetString(2),
            ReduceAnimations = reader.GetInt64(3) != 0,
            LaunchAtStartup = reader.GetInt64(4) != 0,
            StartMinimized = reader.GetInt64(5) != 0,
            GraphHistoryDurationMinutes = reader.GetInt32(6),
            CompactLayout = reader.GetInt64(7) != 0,
            NotificationsEnabled = reader.GetInt64(8) != 0,
            SidebarCollapsed = reader.GetInt64(9) != 0,
            CpuWarningThresholdPercent = reader.GetInt32(10),
            MemoryWarningThresholdPercent = reader.GetInt32(11)
        };
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AppSettings (Id, MonitoringRefreshIntervalSeconds, ThemeMode, AccentColor, ReduceAnimations, LaunchAtStartup, StartMinimized, GraphHistoryDurationMinutes, CompactLayout, NotificationsEnabled, SidebarCollapsed, CpuWarningThresholdPercent, MemoryWarningThresholdPercent)
            VALUES (1, $interval, $theme, $accent, $reduceAnimations, $launchAtStartup, $startMinimized, $historyMinutes, $compactLayout, $notificationsEnabled, $sidebarCollapsed, $cpuThreshold, $memoryThreshold)
            ON CONFLICT(Id) DO UPDATE SET
                MonitoringRefreshIntervalSeconds = excluded.MonitoringRefreshIntervalSeconds,
                ThemeMode = excluded.ThemeMode,
                AccentColor = excluded.AccentColor,
                ReduceAnimations = excluded.ReduceAnimations,
                LaunchAtStartup = excluded.LaunchAtStartup,
                StartMinimized = excluded.StartMinimized,
                GraphHistoryDurationMinutes = excluded.GraphHistoryDurationMinutes,
                CompactLayout = excluded.CompactLayout,
                NotificationsEnabled = excluded.NotificationsEnabled,
                SidebarCollapsed = excluded.SidebarCollapsed,
                CpuWarningThresholdPercent = excluded.CpuWarningThresholdPercent,
                MemoryWarningThresholdPercent = excluded.MemoryWarningThresholdPercent;
            """;
        command.Parameters.AddWithValue("$interval", settings.MonitoringRefreshIntervalSeconds);
        command.Parameters.AddWithValue("$theme", settings.ThemeMode);
        command.Parameters.AddWithValue("$accent", settings.AccentColor);
        command.Parameters.AddWithValue("$reduceAnimations", settings.ReduceAnimations ? 1 : 0);
        command.Parameters.AddWithValue("$launchAtStartup", settings.LaunchAtStartup ? 1 : 0);
        command.Parameters.AddWithValue("$startMinimized", settings.StartMinimized ? 1 : 0);
        command.Parameters.AddWithValue("$historyMinutes", settings.GraphHistoryDurationMinutes);
        command.Parameters.AddWithValue("$compactLayout", settings.CompactLayout ? 1 : 0);
        command.Parameters.AddWithValue("$notificationsEnabled", settings.NotificationsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$sidebarCollapsed", settings.SidebarCollapsed ? 1 : 0);
        command.Parameters.AddWithValue("$cpuThreshold", settings.CpuWarningThresholdPercent);
        command.Parameters.AddWithValue("$memoryThreshold", settings.MemoryWarningThresholdPercent);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using (var createCommand = connection.CreateCommand())
        {
            createCommand.CommandText = """
                CREATE TABLE IF NOT EXISTS AppSettings (
                    Id INTEGER PRIMARY KEY CHECK (Id = 1),
                    MonitoringRefreshIntervalSeconds INTEGER NOT NULL CHECK (MonitoringRefreshIntervalSeconds IN (1, 2, 5, 10)),
                    ThemeMode TEXT NOT NULL DEFAULT 'Dark',
                    AccentColor TEXT NOT NULL DEFAULT '#4D8DFF',
                    ReduceAnimations INTEGER NOT NULL DEFAULT 0,
                    LaunchAtStartup INTEGER NOT NULL DEFAULT 0,
                    StartMinimized INTEGER NOT NULL DEFAULT 0,
                    GraphHistoryDurationMinutes INTEGER NOT NULL DEFAULT 5,
                    CompactLayout INTEGER NOT NULL DEFAULT 0,
                    NotificationsEnabled INTEGER NOT NULL DEFAULT 1,
                    SidebarCollapsed INTEGER NOT NULL DEFAULT 0,
                    CpuWarningThresholdPercent INTEGER NOT NULL DEFAULT 85,
                    MemoryWarningThresholdPercent INTEGER NOT NULL DEFAULT 85
                );
                """;
            await createCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var columnsCommand = connection.CreateCommand())
        {
            columnsCommand.CommandText = "PRAGMA table_info(AppSettings);";
            await using var reader = await columnsCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add(reader.GetString(1));
            }
        }

        var migrations = new (string Name, string Sql)[]
        {
            ("ThemeMode", "ALTER TABLE AppSettings ADD COLUMN ThemeMode TEXT NOT NULL DEFAULT 'Dark';"),
            ("AccentColor", "ALTER TABLE AppSettings ADD COLUMN AccentColor TEXT NOT NULL DEFAULT '#4D8DFF';"),
            ("ReduceAnimations", "ALTER TABLE AppSettings ADD COLUMN ReduceAnimations INTEGER NOT NULL DEFAULT 0;"),
            ("LaunchAtStartup", "ALTER TABLE AppSettings ADD COLUMN LaunchAtStartup INTEGER NOT NULL DEFAULT 0;"),
            ("StartMinimized", "ALTER TABLE AppSettings ADD COLUMN StartMinimized INTEGER NOT NULL DEFAULT 0;"),
            ("GraphHistoryDurationMinutes", "ALTER TABLE AppSettings ADD COLUMN GraphHistoryDurationMinutes INTEGER NOT NULL DEFAULT 5;"),
            ("CompactLayout", "ALTER TABLE AppSettings ADD COLUMN CompactLayout INTEGER NOT NULL DEFAULT 0;"),
            ("NotificationsEnabled", "ALTER TABLE AppSettings ADD COLUMN NotificationsEnabled INTEGER NOT NULL DEFAULT 1;"),
            ("SidebarCollapsed", "ALTER TABLE AppSettings ADD COLUMN SidebarCollapsed INTEGER NOT NULL DEFAULT 0;"),
            ("CpuWarningThresholdPercent", "ALTER TABLE AppSettings ADD COLUMN CpuWarningThresholdPercent INTEGER NOT NULL DEFAULT 85;"),
            ("MemoryWarningThresholdPercent", "ALTER TABLE AppSettings ADD COLUMN MemoryWarningThresholdPercent INTEGER NOT NULL DEFAULT 85;")
        };
        foreach (var (name, sql) in migrations)
        {
            if (!columns.Contains(name))
            {
                await using var migrationCommand = connection.CreateCommand();
                migrationCommand.CommandText = sql;
                await migrationCommand.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }
}
