using AgePilot.Core.History;
using AgePilot.Core.Recommendations;

namespace AgePilot.Core.Rules;

public interface ICoachRule
{
    string Id { get; }

    Recommendation? Evaluate(GameState state, GameHistory history);
}
