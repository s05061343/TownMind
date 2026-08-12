namespace AgePilot.Core.Planning;

public static class GamePlanValidator
{
    private static readonly HashSet<string> Fields = new(StringComparer.OrdinalIgnoreCase)
    {
        "food", "wood", "gold", "stone", "population", "populationCap", "age", "mapArchetype", "waterRatio",
    };
    private static readonly HashSet<string> Operators = new(StringComparer.OrdinalIgnoreCase)
    {
        "eq", "ne", "gt", "gte", "lt", "lte", "confirmed",
    };

    public static PlanningResult Validate(GamePlan? plan, DateTimeOffset now)
    {
        if (plan is null) return new(null, "模型未回傳計畫");
        if (string.IsNullOrWhiteSpace(plan.PlanId) || string.IsNullOrWhiteSpace(plan.Strategy) ||
            string.IsNullOrWhiteSpace(plan.CurrentGoal) || string.IsNullOrWhiteSpace(plan.Reason))
            return new(null, "計畫缺少必要文字欄位");
        if (plan.Confidence is < 0 or > 1) return new(null, "計畫信心超出範圍");
        if (plan.ExpiresAt <= now || plan.ExpiresAt <= plan.CreatedAt || plan.ExpiresAt - plan.CreatedAt > TimeSpan.FromSeconds(60))
            return new(null, "計畫期限無效");
        if (plan.Actions is null || plan.Actions.Count == 0) return new(null, "計畫沒有動作");
        foreach (var action in plan.Actions)
        {
            if (action.Priority is < 0 or > 100 || string.IsNullOrWhiteSpace(action.Reason))
                return new(null, "動作優先度或理由無效");
            if (action.Quantity is < 0 or > 20 || action.TargetPopulationCap is < 0 or > 500 ||
                action.TargetFoodWorkers is < 0 or > 200 || action.TargetWoodWorkers is < 0 or > 200 ||
                action.TargetGoldWorkers is < 0 or > 200 || action.TargetStoneWorkers is < 0 or > 200 ||
                action.TargetResourceAmount is < 0 or > 100000 || action.RecheckSeconds is < 5 or > 120)
                return new(null, "動作量化目標超出合理範圍");
            var targetWorkers = action.TargetFoodWorkers + action.TargetWoodWorkers + action.TargetGoldWorkers + action.TargetStoneWorkers;
            if (action.TargetPopulationCap > 0 && targetWorkers > action.TargetPopulationCap)
                return new(null, "資源村民目標超過人口上限目標");
            foreach (var condition in (action.Preconditions ?? []).Concat(action.CompletionConditions ?? []))
                if (!Fields.Contains(condition.Field) || !Operators.Contains(condition.Operator) || string.IsNullOrWhiteSpace(condition.Value))
                    return new(null, "動作條件不在白名單");
        }
        return new(plan);
    }
}
