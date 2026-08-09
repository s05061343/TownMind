using AgePilot.Core.History;
using AgePilot.Core.Recommendations;

namespace AgePilot.Core.Rules;

public sealed class GoldLowForCastleRule : ICoachRule
{
    public string Id => "R005";

    public Recommendation? Evaluate(GameState state, GameHistory history)
    {
        if (state.Age != GameAge.Feudal || state.Food?.IsUsable != true || state.Gold?.IsUsable != true ||
            state.Food.Value < 600 || state.Gold.Value >= 180) return null;
        var confidence = Math.Min(state.Food.Confidence, state.Gold.Confidence);
        return new Recommendation(Id, CoachSeverity.Suggestion, "準備採金",
            "食物已接近升城需求，黃金稍微不足，可以增加幾位採金村民。", 72, confidence, TimeSpan.FromSeconds(60));
    }
}

public sealed class CastleReadyRule : ICoachRule
{
    public string Id => "R006";

    public Recommendation? Evaluate(GameState state, GameHistory history)
    {
        if (state.Age != GameAge.Feudal || state.Food?.IsUsable != true || state.Gold?.IsUsable != true ||
            state.Food.Value < 800 || state.Gold.Value < 200) return null;
        var confidence = Math.Min(state.Food.Confidence, state.Gold.Confidence);
        return new Recommendation(Id, CoachSeverity.Suggestion, "可以考慮升城堡時代",
            "食物與黃金已經到位，經濟舒服時可以進入城堡時代。", 80, confidence, TimeSpan.FromSeconds(90));
    }
}

public sealed class ImperialReadyRule : ICoachRule
{
    public string Id => "R009";

    public Recommendation? Evaluate(GameState state, GameHistory history)
    {
        if (state.Age != GameAge.Castle || state.Food?.IsUsable != true || state.Gold?.IsUsable != true ||
            state.Food.Value < 1000 || state.Gold.Value < 800) return null;
        var confidence = Math.Min(state.Food.Confidence, state.Gold.Confidence);
        return new Recommendation(Id, CoachSeverity.Suggestion, "可以考慮升帝王時代",
            "升級所需資源已經到位，可以依目前安全狀況決定是否升級。", 78, confidence, TimeSpan.FromSeconds(120));
    }
}
