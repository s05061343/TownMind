using AgePilot.Core.History;
using AgePilot.Core.Recommendations;

namespace AgePilot.Core.Rules;

public sealed class WoodOverflowRule : ICoachRule
{
    public string Id => "R003";

    public Recommendation? Evaluate(GameState state, GameHistory history)
    {
        if (state.Wood?.IsUsable != true || state.Food?.IsUsable != true ||
            state.Wood.Value < 700 || state.Wood.Value <= state.Food.Value)
        {
            return null;
        }

        var confidence = Math.Min(state.Wood.Confidence, state.Food.Confidence);
        return new Recommendation(Id, CoachSeverity.Suggestion, "木材持續偏多",
            "可以考慮增加一些農田，把木材轉成穩定的食物收入。", 50, confidence,
            TimeSpan.FromSeconds(90));
    }
}
