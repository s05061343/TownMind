using AgePilot.Core.History;
using AgePilot.Core.Recommendations;

namespace AgePilot.Core.Rules;

public sealed class PopulationCriticalRule : ICoachRule
{
    public string Id => "R002";

    public Recommendation? Evaluate(GameState state, GameHistory history)
    {
        if (!TryGetPopulation(state, out var current, out var cap, out var confidence) || current < cap)
        {
            return null;
        }

        return new Recommendation(Id, CoachSeverity.Critical, "人口已滿",
            "目前無法增加人口，先安排村民建造房屋。", 100, confidence, TimeSpan.FromSeconds(45));
    }

    internal static bool TryGetPopulation(
        GameState state,
        out int current,
        out int cap,
        out double confidence)
    {
        current = state.Population?.Value ?? 0;
        cap = state.PopulationCap?.Value ?? 0;
        confidence = Math.Min(state.Population?.Confidence ?? 0, state.PopulationCap?.Confidence ?? 0);
        return state.Population?.IsUsable == true && state.PopulationCap?.IsUsable == true && cap > 0;
    }
}

public sealed class PopulationLowRule : ICoachRule
{
    public string Id => "R001";

    public Recommendation? Evaluate(GameState state, GameHistory history)
    {
        if (!PopulationCriticalRule.TryGetPopulation(state, out var current, out var cap, out var confidence))
        {
            return null;
        }

        var remaining = cap - current;
        if (remaining is < 1 or > 5)
        {
            return null;
        }

        return new Recommendation(Id, CoachSeverity.Warning, "準備房屋",
            $"人口空間只剩 {remaining}，可以先安排一位村民建造房屋。", 90, confidence,
            TimeSpan.FromSeconds(45));
    }
}
