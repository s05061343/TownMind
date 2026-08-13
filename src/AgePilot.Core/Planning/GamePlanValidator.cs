namespace AgePilot.Core.Planning;

public static class GamePlanValidator
{
    private static readonly HashSet<string> Fields = new(StringComparer.OrdinalIgnoreCase)
    { "food", "wood", "gold", "stone", "population", "populationCap", "age", "mapArchetype", "waterRatio" };
    private static readonly HashSet<string> Operators = new(StringComparer.OrdinalIgnoreCase)
    { "eq", "ne", "gt", "gte", "lt", "lte", "confirmed" };

    public static PlanningResult Validate(GamePlan? plan, DateTimeOffset now)
    {
        if (plan is null) return new(null, "規劃結果不可為空白");
        if (string.IsNullOrWhiteSpace(plan.PlanId) || string.IsNullOrWhiteSpace(plan.Strategy) ||
            string.IsNullOrWhiteSpace(plan.CurrentGoal) || string.IsNullOrWhiteSpace(plan.Reason))
            return new(null, "規劃缺少必要欄位");
        if (plan.Confidence is < 0 or > 1) return new(null, "規劃信心值超出範圍");
        if (plan.ExpiresAt <= now || plan.ExpiresAt <= plan.CreatedAt || plan.ExpiresAt - plan.CreatedAt > TimeSpan.FromSeconds(60))
            return new(null, "規劃有效期限無效");
        if (plan.Actions is null || plan.Actions.Count == 0) return new(null, "規劃缺少動作");
        var decisions = new[] { plan.MajorDecision, plan.MediumDecision, plan.MinorDecision };
        if (decisions.Any(x => x is not null) && decisions.Any(x => x is null))
            return new(null, "階層計畫必須同時包含大、中、小判斷");
        if (plan.MajorDecision is not null)
        {
            if (plan.MajorDecision.Level != DecisionLevel.Major || plan.MediumDecision!.Level != DecisionLevel.Medium ||
                plan.MinorDecision!.Level != DecisionLevel.Minor) return new(null, "階層計畫層級無效");
            if (decisions.Select(x => x!.NodeId).Distinct(StringComparer.Ordinal).Count() != 3 ||
                decisions.Any(x => !ValidDecision(x!))) return new(null, "階層計畫節點無效");
        }
        if (plan.VisualDecision is { } decision)
        {
            if (decision.Confidence is < 0 or > 1 || decision.RecheckAfterMs is < 250 or > 30000 ||
                string.IsNullOrWhiteSpace(decision.Assessment) || string.IsNullOrWhiteSpace(decision.Goal) ||
                string.IsNullOrWhiteSpace(decision.Reason) || string.IsNullOrWhiteSpace(decision.ExpectedResult))
                return new(null, "視覺決策欄位無效");
            if (!CoordinatesAreSafe(decision.Action)) return new(null, "滑鼠座標或座標空間無效");
            if (decision.Action.Target.Length > 80) return new(null, "滑鼠目標描述過長");
            if (decision.Action.Tool is not (VisualToolKind.Observe or VisualToolKind.Wait) &&
                string.IsNullOrWhiteSpace(decision.Action.Target)) return new(null, "滑鼠動作缺少目標證據");
        }
        foreach (var action in plan.Actions)
        {
            if (action.Priority is < 0 or > 100 || string.IsNullOrWhiteSpace(action.Reason)) return new(null, "規劃動作無效");
            if (action.Quantity is < 0 or > 20 || action.TargetPopulationCap is < 0 or > 500 ||
                action.TargetFoodWorkers is < 0 or > 200 || action.TargetWoodWorkers is < 0 or > 200 ||
                action.TargetGoldWorkers is < 0 or > 200 || action.TargetStoneWorkers is < 0 or > 200 ||
                action.TargetResourceAmount is < 0 or > 100000 || action.RecheckSeconds is < 5 or > 120)
                return new(null, "規劃動作數值超出範圍");
            if (action.TargetId.Length > 80 || action.TargetId.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
                return new(null, "目標 ID 格式無效");
            if (action.Intent is PlannedActionKind.BuildBuilding or PlannedActionKind.ResearchTechnology or
                PlannedActionKind.AdvanceAge or PlannedActionKind.QueueUnit && string.IsNullOrWhiteSpace(action.TargetId))
                return new(null, "規劃動作缺少目標 ID");
            if (action.TargetPopulationCap > 0 && action.TargetFoodWorkers + action.TargetWoodWorkers +
                action.TargetGoldWorkers + action.TargetStoneWorkers > action.TargetPopulationCap)
                return new(null, "工作分配超過人口目標");
            foreach (var condition in (action.Preconditions ?? []).Concat(action.CompletionConditions ?? []))
                if (!Fields.Contains(condition.Field) || !Operators.Contains(condition.Operator) || string.IsNullOrWhiteSpace(condition.Value))
                    return new(null, "規劃條件不在允許範圍");
        }
        return new(plan);
    }

    private static bool ValidDecision(DecisionNode node) =>
        !string.IsNullOrWhiteSpace(node.NodeId) && node.NodeId.Length <= 80 &&
        node.NodeId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') &&
        !string.IsNullOrWhiteSpace(node.Objective) && !string.IsNullOrWhiteSpace(node.Reason) &&
        !string.IsNullOrWhiteSpace(node.Evidence) && !string.IsNullOrWhiteSpace(node.CompletionCondition) &&
        !string.IsNullOrWhiteSpace(node.FailureCondition);

    private static bool CoordinatesAreSafe(VisualToolAction action)
    {
        static bool Unit(double value) => value is >= 0 and <= 1;
        if (action.Space == VisualCoordinateSpace.CommandGrid)
            return action.Tool == VisualToolKind.LeftClick && action.Row is >= 1 and <= 3 && action.Column is >= 1 and <= 5;
        if (action.Space == VisualCoordinateSpace.Minimap && action.Tool == VisualToolKind.Drag) return false;
        return action.Tool switch
        {
            VisualToolKind.LeftClick or VisualToolKind.RightClick => Unit(action.X) && Unit(action.Y),
            VisualToolKind.Drag => action.Space == VisualCoordinateSpace.Panorama && Unit(action.X) && Unit(action.Y) && Unit(action.EndX) && Unit(action.EndY),
            _ => true,
        };
    }
}
