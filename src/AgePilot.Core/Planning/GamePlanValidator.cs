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
        if (plan.VisualDecision is { } decision)
        {
            if (decision.Confidence is < 0 or > 1 || decision.RecheckAfterMs is < 250 or > 30000 ||
                string.IsNullOrWhiteSpace(decision.Assessment) || string.IsNullOrWhiteSpace(decision.Goal) ||
                string.IsNullOrWhiteSpace(decision.Reason) || string.IsNullOrWhiteSpace(decision.ExpectedResult))
                return new(null, "視覺玩家決策欄位無效");
            if (!CoordinatesAreSafe(decision.Action)) return new(null, "視覺玩家座標超出 normalized 範圍");
            if (decision.Action.Keys.Count > 6 || decision.Action.Keys.Any(key =>
                    key.Length > 16 || key.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '+' or '.' or '-' or '='))))
                return new(null, "視覺玩家按鍵不在安全格式");
            if (decision.Action.Tool == VisualToolKind.KeySequence)
            {
                try { foreach (var chord in decision.Action.Keys) _ = Automation.InputSequence.Parse(chord); }
                catch (InvalidDataException) { return new(null, "視覺玩家按鍵不在白名單"); }
            }
        }
        foreach (var action in plan.Actions)
        {
            if (action.Priority is < 0 or > 100 || string.IsNullOrWhiteSpace(action.Reason))
                return new(null, "動作優先度或理由無效");
            if (action.Quantity is < 0 or > 20 || action.TargetPopulationCap is < 0 or > 500 ||
                action.TargetFoodWorkers is < 0 or > 200 || action.TargetWoodWorkers is < 0 or > 200 ||
                action.TargetGoldWorkers is < 0 or > 200 || action.TargetStoneWorkers is < 0 or > 200 ||
                action.TargetResourceAmount is < 0 or > 100000 || action.RecheckSeconds is < 5 or > 120)
                return new(null, "動作量化目標超出合理範圍");
            if (action.TargetId.Length > 80 || action.TargetId.Any(character =>
                    !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
                return new(null, "動作目標 ID 不是安全白名單識別字");
            if (action.Intent is PlannedActionKind.BuildBuilding or PlannedActionKind.ResearchTechnology or
                PlannedActionKind.AdvanceAge or PlannedActionKind.QueueUnit && string.IsNullOrWhiteSpace(action.TargetId))
                return new(null, "通用動作缺少目標 ID");
            var targetWorkers = action.TargetFoodWorkers + action.TargetWoodWorkers + action.TargetGoldWorkers + action.TargetStoneWorkers;
            if (action.TargetPopulationCap > 0 && targetWorkers > action.TargetPopulationCap)
                return new(null, "資源村民目標超過人口上限目標");
            foreach (var condition in (action.Preconditions ?? []).Concat(action.CompletionConditions ?? []))
                if (!Fields.Contains(condition.Field) || !Operators.Contains(condition.Operator) || string.IsNullOrWhiteSpace(condition.Value))
                    return new(null, "動作條件不在白名單");
        }
        return new(plan);
    }

    private static bool CoordinatesAreSafe(VisualToolAction action)
    {
        static bool Unit(double value) => value is >= 0 and <= 1;
        return action.Tool switch
        {
            VisualToolKind.LeftClick or VisualToolKind.RightClick => Unit(action.X) && Unit(action.Y),
            VisualToolKind.Drag => Unit(action.X) && Unit(action.Y) && Unit(action.EndX) && Unit(action.EndY),
            _ => true,
        };
    }
}
