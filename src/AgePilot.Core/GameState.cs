using AgePilot.Core.Observations;

namespace AgePilot.Core;

public sealed class GameState
{
    public TimeSpan? GameTime { get; init; }

    public ObservedValue<int>? Food { get; init; }

    public ObservedValue<int>? Wood { get; init; }

    public ObservedValue<int>? Gold { get; init; }

    public ObservedValue<int>? Stone { get; init; }

    public ObservedValue<int>? Population { get; init; }

    public ObservedValue<int>? PopulationCap { get; init; }
}
