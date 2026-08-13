namespace AgePilot.Core.Planning;

public static class GamePlanValidator
{
    public static PlanningResult Validate(GamePlan? plan, DateTimeOffset now)
    {
        if (plan is null) return new(null, "規劃結果不可為空白");
        if (string.IsNullOrWhiteSpace(plan.PlanId) || string.IsNullOrWhiteSpace(plan.Strategy) ||
            string.IsNullOrWhiteSpace(plan.CurrentGoal) || string.IsNullOrWhiteSpace(plan.Reason))
            return new(null, "規劃缺少必要欄位");
        if (plan.Confidence is < 0 or > 1) return new(null, "規劃信心值超出範圍");
        if (plan.ExpiresAt <= now || plan.ExpiresAt <= plan.CreatedAt || plan.ExpiresAt - plan.CreatedAt > TimeSpan.FromSeconds(60))
            return new(null, "規劃有效期限無效");
        if (plan.MajorDecision is null || plan.MediumDecision is null || plan.MinorDecision is null)
            return new(null, "階層計畫缺少必要判斷層級");
        if (plan.MajorDecision.Level != DecisionLevel.Major || plan.MediumDecision.Level != DecisionLevel.Medium ||
            plan.MinorDecision.Level != DecisionLevel.Minor) return new(null, "階層計畫層級無效");
        var decisions = new[] { plan.MajorDecision, plan.MediumDecision, plan.MinorDecision };
        if (decisions.Select(x => x.NodeId).Distinct(StringComparer.Ordinal).Count() != 3 ||
            decisions.Any(x => !ValidDecision(x))) return new(null, "階層計畫節點無效");
        if (plan.VisualDecision is { } decision)
        {
            if (decision.Confidence is < 0 or > 1 || decision.RecheckAfterMs is < 250 or > 30000 ||
                string.IsNullOrWhiteSpace(decision.Assessment) || string.IsNullOrWhiteSpace(decision.Goal) ||
                string.IsNullOrWhiteSpace(decision.Reason) || string.IsNullOrWhiteSpace(decision.ExpectedResult))
                return new(null, "視覺決策欄位無效");
            if (!Enum.IsDefined(decision.Action.Kind)) return new(null, "動作名稱無效");
            if (decision.Action.Quantity is < 1 or > 10) return new(null, "動作數量超出範圍");
            if (decision.Action.Reason.Length > 200) return new(null, "動作理由過長");
        }
        return new(plan);
    }

    private static bool ValidDecision(DecisionNode node) =>
        !string.IsNullOrWhiteSpace(node.NodeId) && node.NodeId.Length <= 80 &&
        node.NodeId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') &&
        !string.IsNullOrWhiteSpace(node.Objective) && !string.IsNullOrWhiteSpace(node.Reason) &&
        !string.IsNullOrWhiteSpace(node.Evidence) && !string.IsNullOrWhiteSpace(node.CompletionCondition) &&
        !string.IsNullOrWhiteSpace(node.FailureCondition);
}
