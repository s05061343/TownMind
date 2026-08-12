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
            foreach (var condition in (action.Preconditions ?? []).Concat(action.CompletionConditions ?? []))
                if (!Fields.Contains(condition.Field) || !Operators.Contains(condition.Operator) || string.IsNullOrWhiteSpace(condition.Value))
                    return new(null, "動作條件不在白名單");
        }
        return new(plan);
    }
}
