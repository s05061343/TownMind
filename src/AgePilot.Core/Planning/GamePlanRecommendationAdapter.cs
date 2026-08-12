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
                FormatTitle(action), FormatDescription(action), action.Priority,
                Math.Clamp(plan.Confidence, 0, 1), TimeSpan.Zero)).ToArray();
    }

    private static string FormatTitle(PlannedAction action) => action.Intent switch
    {
        PlannedActionKind.QueueVillager => action.Quantity > 0 ? $"排入 {action.Quantity} 位村民" : "維持村民生產",
        PlannedActionKind.BuildHouse => action.Quantity > 0 ? $"建造 {action.Quantity} 間房屋" : "準備房屋",
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

    private static string FormatDescription(PlannedAction action)
    {
        var parts = new List<string> { action.Reason };
        if (action.TargetPopulationCap > 0) parts.Add($"人口上限目標：{action.TargetPopulationCap}");
        var workers = new List<string>();
        if (action.TargetFoodWorkers > 0) workers.Add($"食物 {action.TargetFoodWorkers} 人");
        if (action.TargetWoodWorkers > 0) workers.Add($"木材 {action.TargetWoodWorkers} 人");
        if (action.TargetGoldWorkers > 0) workers.Add($"黃金 {action.TargetGoldWorkers} 人");
        if (action.TargetStoneWorkers > 0) workers.Add($"石頭 {action.TargetStoneWorkers} 人");
        if (workers.Count > 0) parts.Add($"目標配置（非目前實測）：{string.Join("、", workers)}");
        if (action.TargetResourceAmount > 0) parts.Add($"資源檢查點：{action.TargetResourceAmount}");
        if (!string.IsNullOrWhiteSpace(action.SuccessCondition)) parts.Add($"完成條件：{action.SuccessCondition}");
        parts.Add($"約 {action.RecheckSeconds} 秒後重新評估");
        return string.Join("\n", parts);
    }
}
