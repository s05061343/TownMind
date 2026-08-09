using AgePilot.Core.Recommendations;

namespace AgePilot.Core.Persistence;

public interface ISessionRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<long> StartSessionAsync(string profile, DateTimeOffset startedAt, CancellationToken cancellationToken = default);
    Task AddSnapshotAsync(long sessionId, DateTimeOffset capturedAt, GameState state, CancellationToken cancellationToken = default);
    Task AddRecommendationAsync(long sessionId, DateTimeOffset createdAt, Recommendation recommendation, CancellationToken cancellationToken = default);
    Task EndSessionAsync(long sessionId, DateTimeOffset endedAt, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SessionSummary>> GetRecentSessionsAsync(int maximum = 20, CancellationToken cancellationToken = default);
}

public sealed record SessionSummary(
    long Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string Profile,
    int SnapshotCount,
    int RecommendationCount,
    int? PeakFood,
    int? PeakWood,
    int? PeakGold,
    int? PeakStone,
    int? PeakPopulation);
