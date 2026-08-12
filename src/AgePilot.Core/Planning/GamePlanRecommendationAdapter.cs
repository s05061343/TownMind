using AgePilot.Core.Recommendations;

namespace AgePilot.Core.Planning;

public static class GamePlanRecommendationAdapter
{
    public static IReadOnlyList<Recommendation> Convert(GamePlan? plan)
    {
        if (plan is null) return [];
        return plan.Actions.OrderByDescending(x => x.Priority).Take(3).Select((action, index) =>
            new Recommendation($"plan:{plan.PlanId}:{index}",
                action.Priority >= 90 ? CoachSeverity.Warning : CoachSeverity.Suggestion,
                Format(action.Intent), action.Reason, action.Priority,
                Math.Clamp(plan.Confidence, 0, 1), TimeSpan.Zero)).ToArray();
    }

    private static string Format(PlannedActionKind kind) => kind switch
    {
        PlannedActionKind.QueueVillager => "維持村民生產",
        PlannedActionKind.BuildHouse => "準備房屋",
        PlannedActionKind.GatherFood => "增加食物採集",
        PlannedActionKind.GatherWood => "增加木材採集",
        PlannedActionKind.GatherGold => "增加黃金採集",
        PlannedActionKind.AdvanceFeudal => "準備升封建時代",
        PlannedActionKind.AdvanceCastle => "準備升城堡時代",
        PlannedActionKind.DevelopWaterEconomy => "發展水上經濟",
        PlannedActionKind.Scout => "持續偵察",
        PlannedActionKind.Reobserve => "等待更多戰場資訊",
        _ => "維持目前計畫",
    };
}
