using System.Globalization;
using System.Text.Json;
using FixMate.Core.Interfaces;
using FixMate.Core.Models;
using Microsoft.Data.Sqlite;

namespace FixMate.Infrastructure.Storage;

public sealed class SqliteNetworkHistoryStore : INetworkHistoryStore
{
    private const int HistoryLimit = 200;
    private readonly string _connectionString;

    public SqliteNetworkHistoryStore()
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

    public async Task<IReadOnlyList<NetworkDiagnosticResult>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Tool, Target, Succeeded, Summary, Details, CompletedAt FROM NetworkDiagnosticHistory ORDER BY CompletedAt DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", HistoryLimit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<NetworkDiagnosticResult>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new NetworkDiagnosticResult(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt64(2) != 0,
                reader.GetString(3),
                JsonSerializer.Deserialize<string[]>(reader.GetString(4)) ?? [],
                DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return results;
    }

    public async Task SaveResultAsync(NetworkDiagnosticResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var transaction = connection.BeginTransaction();
        await using (var insertCommand = connection.CreateCommand())
        {
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = "INSERT INTO NetworkDiagnosticHistory (Tool, Target, Succeeded, Summary, Details, CompletedAt) VALUES ($tool, $target, $succeeded, $summary, $details, $completedAt);";
            insertCommand.Parameters.AddWithValue("$tool", result.Tool);
            insertCommand.Parameters.AddWithValue("$target", result.Target);
            insertCommand.Parameters.AddWithValue("$succeeded", result.Succeeded ? 1 : 0);
            insertCommand.Parameters.AddWithValue("$summary", result.Summary);
            insertCommand.Parameters.AddWithValue("$details", JsonSerializer.Serialize(result.Details));
            insertCommand.Parameters.AddWithValue("$completedAt", result.CompletedAt.ToString("O", CultureInfo.InvariantCulture));
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var trimCommand = connection.CreateCommand())
        {
            trimCommand.Transaction = transaction;
            trimCommand.CommandText = "DELETE FROM NetworkDiagnosticHistory WHERE Id NOT IN (SELECT Id FROM NetworkDiagnosticHistory ORDER BY CompletedAt DESC, Id DESC LIMIT $limit);";
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
            CREATE TABLE IF NOT EXISTS NetworkDiagnosticHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Tool TEXT NOT NULL,
                Target TEXT NOT NULL,
                Succeeded INTEGER NOT NULL,
                Summary TEXT NOT NULL,
                Details TEXT NOT NULL,
                CompletedAt TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
