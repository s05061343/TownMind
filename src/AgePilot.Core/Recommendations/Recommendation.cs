namespace AgePilot.Core.Recommendations;

public sealed record Recommendation(
    string Id,
    CoachSeverity Severity,
    string Title,
    string Description,
    int Priority,
    double Confidence,
    TimeSpan Cooldown);
