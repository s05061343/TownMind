using AgePilot.Core;
using AgePilot.Core.Persistence;
using AgePilot.Core.Recommendations;
using Microsoft.Data.Sqlite;

namespace AgePilot.Infrastructure.Persistence;

public sealed class SqliteSessionRepository(string databasePath) : ISessionRepository
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared,
    }.ToString();

    public string DatabasePath { get; } = databasePath;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;
            CREATE TABLE IF NOT EXISTS GameSessions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StartedAt TEXT NOT NULL,
                EndedAt TEXT NULL,
                Profile TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS GameSnapshots (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId INTEGER NOT NULL REFERENCES GameSessions(Id) ON DELETE CASCADE,
                CapturedAt TEXT NOT NULL,
                Food INTEGER NULL,
                Wood INTEGER NULL,
                Gold INTEGER NULL,
                Stone INTEGER NULL,
                Population INTEGER NULL,
                PopulationCap INTEGER NULL
            );
            CREATE INDEX IF NOT EXISTS IX_GameSnapshots_Session_Captured
                ON GameSnapshots(SessionId, CapturedAt);
            CREATE TABLE IF NOT EXISTS RecommendationEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId INTEGER NOT NULL REFERENCES GameSessions(Id) ON DELETE CASCADE,
                CreatedAt TEXT NOT NULL,
                RuleId TEXT NOT NULL,
                Severity TEXT NOT NULL,
                Title TEXT NOT NULL,
                Description TEXT NOT NULL,
                Priority INTEGER NOT NULL,
                Confidence REAL NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_RecommendationEvents_Session_Created
                ON RecommendationEvents(SessionId, CreatedAt);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> StartSessionAsync(string profile, DateTimeOffset startedAt, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO GameSessions (StartedAt, Profile) VALUES ($startedAt, $profile); SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$startedAt", startedAt.ToString("O"));
        command.Parameters.AddWithValue("$profile", profile);
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException("Session id was not returned."));
    }

    public async Task AddSnapshotAsync(long sessionId, DateTimeOffset capturedAt, GameState state, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO GameSnapshots
                (SessionId, CapturedAt, Food, Wood, Gold, Stone, Population, PopulationCap)
            VALUES
                ($sessionId, $capturedAt, $food, $wood, $gold, $stone, $population, $populationCap);
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId);
        command.Parameters.AddWithValue("$capturedAt", capturedAt.ToString("O"));
        AddNullable(command, "$food", state.Food?.IsUsable == true ? state.Food.Value : null);
        AddNullable(command, "$wood", state.Wood?.IsUsable == true ? state.Wood.Value : null);
        AddNullable(command, "$gold", state.Gold?.IsUsable == true ? state.Gold.Value : null);
        AddNullable(command, "$stone", state.Stone?.IsUsable == true ? state.Stone.Value : null);
        AddNullable(command, "$population", state.Population?.IsUsable == true ? state.Population.Value : null);
        AddNullable(command, "$populationCap", state.PopulationCap?.IsUsable == true ? state.PopulationCap.Value : null);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddRecommendationAsync(long sessionId, DateTimeOffset createdAt, Recommendation recommendation, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RecommendationEvents
                (SessionId, CreatedAt, RuleId, Severity, Title, Description, Priority, Confidence)
            VALUES
                ($sessionId, $createdAt, $ruleId, $severity, $title, $description, $priority, $confidence);
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId);
        command.Parameters.AddWithValue("$createdAt", createdAt.ToString("O"));
        command.Parameters.AddWithValue("$ruleId", recommendation.Id);
        command.Parameters.AddWithValue("$severity", recommendation.Severity.ToString());
        command.Parameters.AddWithValue("$title", recommendation.Title);
        command.Parameters.AddWithValue("$description", recommendation.Description);
        command.Parameters.AddWithValue("$priority", recommendation.Priority);
        command.Parameters.AddWithValue("$confidence", recommendation.Confidence);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task EndSessionAsync(long sessionId, DateTimeOffset endedAt, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE GameSessions SET EndedAt = $endedAt WHERE Id = $id AND EndedAt IS NULL;";
        command.Parameters.AddWithValue("$endedAt", endedAt.ToString("O"));
        command.Parameters.AddWithValue("$id", sessionId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SessionSummary>> GetRecentSessionsAsync(int maximum = 20, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.Id, s.StartedAt, s.EndedAt, s.Profile,
                   COUNT(DISTINCT gs.Id), COUNT(DISTINCT re.Id),
                   MAX(gs.Food), MAX(gs.Wood), MAX(gs.Gold), MAX(gs.Stone), MAX(gs.Population)
            FROM GameSessions s
            LEFT JOIN GameSnapshots gs ON gs.SessionId = s.Id
            LEFT JOIN RecommendationEvents re ON re.SessionId = s.Id
            GROUP BY s.Id
            ORDER BY s.StartedAt DESC
            LIMIT $maximum;
            """;
        command.Parameters.AddWithValue("$maximum", Math.Clamp(maximum, 1, 100));
        var results = new List<SessionSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new SessionSummary(
                reader.GetInt64(0),
                DateTimeOffset.Parse(reader.GetString(1)),
                reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2)),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetInt32(8),
                reader.IsDBNull(9) ? null : reader.GetInt32(9),
                reader.IsDBNull(10) ? null : reader.GetInt32(10)));
        }
        return results;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static void AddNullable(SqliteCommand command, string name, int? value) =>
        command.Parameters.AddWithValue(name, value is null ? DBNull.Value : value.Value);

    public static SqliteSessionRepository CreateDefault()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new SqliteSessionRepository(Path.Combine(appData, "AgePilot", "agepilot.db"));
    }
}
