using AgePilot.Core.History;
using AgePilot.Core.Recommendations;

namespace AgePilot.Core.Rules;

public sealed class CoachEngine(IEnumerable<ICoachRule> rules)
{
    private readonly IReadOnlyList<ICoachRule> _rules = rules.ToArray();

    public IReadOnlyList<Recommendation> Evaluate(GameState state, GameHistory history, int maximum = 3)
    {
        return _rules
            .Select(rule => rule.Evaluate(state, history))
            .OfType<Recommendation>()
            .Where(recommendation => recommendation.Confidence >= 0.7)
            .OrderByDescending(recommendation => recommendation.Priority)
            .Take(maximum)
            .ToArray();
    }
}
