using AgePilot.Core.History;
using AgePilot.Core.Recommendations;

namespace AgePilot.Core.Rules;

public sealed class ResourceOverflowRule : ICoachRule
{
    public string Id => "R012";

    public Recommendation? Evaluate(GameState state, GameHistory history)
    {
        var resources = new (string Name, AgePilot.Core.Observations.ObservedValue<int>? Value)[]
        {
            ("食物", state.Food), ("木材", state.Wood), ("黃金", state.Gold), ("石頭", state.Stone),
        };
        var highest = resources
            .Where(item => item.Value?.IsUsable == true && item.Value.Confidence >= 0.7)
            .OrderByDescending(item => item.Value!.Value)
            .FirstOrDefault();
        if (highest.Value?.Value is not { } amount || amount < 1500) return null;

        return new Recommendation(Id, CoachSeverity.Warning, $"{highest.Name}囤積偏高",
            $"目前有 {amount} {highest.Name}，可以考慮轉成科技、單位或經濟建設。", 68,
            highest.Value.Confidence, TimeSpan.FromSeconds(120));
    }
}
