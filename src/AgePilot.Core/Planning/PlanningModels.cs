using AgePilot.Core.Automation;
using AgePilot.Core.History;
using AgePilot.Core.Observations;

namespace AgePilot.Core.Planning;

public enum MapArchetype { Unknown, OpenLand, ClosedLand, Island, Coastal }

public sealed record MapContext(
    MapArchetype Archetype,
    double WaterRatio,
    double ForestRatio,
    double OpenTerrainRatio,
    double VisibleCoverage,
    int LandComponentCount,
    double ChokePointScore,
    double Confidence,
    ObservationStatus Status,
    DateTimeOffset ObservedAt,
    int ConsistentFrames)
{
    public bool IsUsable => Status == ObservationStatus.Confirmed && Confidence >= 0.7 && VisibleCoverage >= 0.15;
}

public sealed record ResourceTrend(double ChangePerMinute, bool IsStalled);

public sealed record GameHistorySummary(
    ResourceTrend? Food,
    ResourceTrend? Wood,
    ResourceTrend? Gold,
    ResourceTrend? Stone,
    int? PopulationChange,
    TimeSpan Window);

public enum PlanUpdateScope { Minor, Medium, Major }

public enum DecisionLevel { Major, Medium, Minor }

public enum DecisionStatus { Active, Completed, Failed, Blocked }

public sealed record DecisionNode(
    string NodeId,
    DecisionLevel Level,
    string Objective,
    string Reason,
    string Evidence,
    string CompletionCondition,
    string FailureCondition,
    DecisionStatus Status = DecisionStatus.Active);

public sealed record StrategyDirective(
    string Strategy,
    GameAge TargetAge);

public sealed record PlanningEvent(
    string Kind,
    string Detail,
    DateTimeOffset At,
    PlanUpdateScope Scope = PlanUpdateScope.Minor,
    string? NodeId = null,
    PreviousActionResult Result = PreviousActionResult.NotApplicable);

public sealed record SituationContext(
    GameState State,
    GameHistorySummary History,
    MapContext? Map,
    GamePlan? PreviousPlan,
    IReadOnlyList<PlanningEvent> RecentEvents,
    DateTimeOffset CapturedAt,
    VisualObservation? Visual = null,
    StrategyDirective? Directive = null,
    PlanUpdateScope AllowedUpdateScope = PlanUpdateScope.Major);

public sealed record VisualImage(string Name, string MimeType, byte[] Data);

public sealed record VisualObservation(
    int FrameWidth,
    int FrameHeight,
    IReadOnlyList<VisualImage> Images,
    string UiLayout,
    string? PreviousAction,
    string? PreviousResult);

public enum VisualToolKind { Observe, Wait, LeftClick, RightClick, Drag }

public enum VisualCoordinateSpace { Panorama, Minimap, CommandGrid }

public enum PreviousActionResult { NotApplicable, Confirmed, Failed, Uncertain }

public sealed record VisualToolAction(
    VisualToolKind Tool,
    VisualCoordinateSpace Space = VisualCoordinateSpace.Panorama,
    string Target = "",
    double X = 0,
    double Y = 0,
    double EndX = 0,
    double EndY = 0,
    int Row = 0,
    int Column = 0);

/// <summary>
/// 模型單輪的決定。依 ADR 0015，Action 是具名的 <see cref="GameAction"/>，模型不再輸出座標；
/// 前一動作的結果也不再由模型回報，改由 <see cref="Automation.ActionOutcomeVerifier"/> 依 OCR 判定。
/// </summary>
public sealed record VisualPlayerDecision(
    string Assessment,
    string Goal,
    string Reason,
    GameAction Action,
    string ExpectedResult,
    int RecheckAfterMs,
    double Confidence);

public sealed record GamePlan(
    string PlanId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string Strategy,
    string CurrentGoal,
    string Reason,
    double Confidence,
    bool ReusedAfterPlanningFailure = false,
    VisualPlayerDecision? VisualDecision = null,
    int Revision = 1,
    DecisionNode? MajorDecision = null,
    DecisionNode? MediumDecision = null,
    DecisionNode? MinorDecision = null,
    PlanUpdateScope RequestedUpdateScope = PlanUpdateScope.Minor);

public sealed record PlanningResult(GamePlan? Plan, string? Error = null)
{
    public bool Success => Plan is not null && Error is null;
}

public interface IStrategicPlanner
{
    Task<PlanningResult> PlanAsync(SituationContext context, CancellationToken cancellationToken);
}

public static class GameHistorySummarizer
{
    public static GameHistorySummary Summarize(GameHistory history, TimeSpan window, DateTimeOffset now)
    {
        var samples = history.Snapshots.Where(x => now - x.CapturedAt <= window).OrderBy(x => x.CapturedAt).ToArray();
        return new(
            Trend(samples, s => s.State.Food), Trend(samples, s => s.State.Wood),
            Trend(samples, s => s.State.Gold), Trend(samples, s => s.State.Stone),
            Delta(samples, s => s.State.Population), window);
    }

    private static ResourceTrend? Trend(GameSnapshot[] samples, Func<GameSnapshot, ObservedValue<int>?> selector)
    {
        var usable = samples.Select(s => (s.CapturedAt, Value: selector(s))).Where(x => x.Value?.IsUsable == true).ToArray();
        if (usable.Length < 2) return null;
        var minutes = (usable[^1].CapturedAt - usable[0].CapturedAt).TotalMinutes;
        if (minutes <= 0) return null;
        var change = (usable[^1].Value!.Value.GetValueOrDefault() - usable[0].Value!.Value.GetValueOrDefault()) / minutes;
        return new(change, Math.Abs(change) < 5);
    }

    private static int? Delta(GameSnapshot[] samples, Func<GameSnapshot, ObservedValue<int>?> selector)
    {
        var usable = samples.Select(selector).Where(x => x?.IsUsable == true).ToArray();
        return usable.Length < 2 ? null : usable[^1]!.Value.GetValueOrDefault() - usable[0]!.Value.GetValueOrDefault();
    }
}
