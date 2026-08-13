using AgePilot.Core.Recommendations;

namespace AgePilot.Core.Planning;

public static class GamePlanRecommendationAdapter
{
    public static IReadOnlyList<Recommendation> Convert(GamePlan? plan) =>
        plan?.MinorDecision is { } minor
            ? [new Recommendation($"plan:{minor.NodeId}",
                minor.Status is DecisionStatus.Failed or DecisionStatus.Blocked ? CoachSeverity.Warning : CoachSeverity.Suggestion,
                minor.Objective, $"{minor.Reason}\n證據：{minor.Evidence}\n完成條件：{minor.CompletionCondition}",
                50, Math.Clamp(plan.Confidence, 0, 1), TimeSpan.Zero)]
            : [];
}
