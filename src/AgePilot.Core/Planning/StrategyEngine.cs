using AgePilot.Core.History;

namespace AgePilot.Core.Planning;

public sealed class StrategyEngine(IStrategicPlanner planner) : IDisposable
{
    private GamePlan? _current;
    private Task<PlanningResult>? _pending;
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;
    private GameAge? _lastAge;
    private bool? _lastPopulationCapped;
    private MapArchetype _lastMap = MapArchetype.Unknown;

    public PlannerRuntimeStatus RuntimeStatus => planner is IPlannerRuntimeStatusSource source
        ? source.RuntimeStatus
        : PlannerRuntimeStatus.NotConfigured("規劃器未提供 runtime 狀態");

    public Task<(GamePlan? Plan, string? Status)> UpdateAsync(
        GameState state, GameHistory history, MapContext? map, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (_pending?.IsCompleted == true)
        {
            var result = _pending.GetAwaiter().GetResult();
            _pending = null;
            if (result.Success) _current = result.Plan;
            else if (_current is not null) _current = _current with { ReusedAfterPlanningFailure = true };
        }

        var populationCapped = state.Population?.IsUsable == true && state.PopulationCap?.IsUsable == true &&
                               state.Population.Value >= state.PopulationCap.Value;
        var changed = state.Age != _lastAge || populationCapped != _lastPopulationCapped ||
                      (map?.IsUsable == true && map.Archetype != _lastMap);
        _lastAge = state.Age; _lastPopulationCapped = populationCapped;
        if (map?.IsUsable == true) _lastMap = map.Archetype;

        if (_pending is null && (changed || now - _lastAttempt >= TimeSpan.FromSeconds(20)))
        {
            _lastAttempt = now;
            var events = changed ? new[] { new PlanningEvent("state_changed", "時代、人口或地圖狀態改變", now) } : [];
            var context = new SituationContext(state, GameHistorySummarizer.Summarize(history, TimeSpan.FromSeconds(120), now),
                map?.IsUsable == true ? map : null, _current, events, now);
            _pending = planner.PlanAsync(context, cancellationToken);
        }

        var current = Current(now);
        return Task.FromResult((current, _pending is not null ? "背景規劃中" : current is null ? "規劃暫時不可用" : null));
    }

    public (GamePlan? Plan, string? Status) Pause(DateTimeOffset now, string status) => (null, status);

    private GamePlan? Current(DateTimeOffset now)
    {
        if (_current is null || _current.ExpiresAt <= now) _current = null;
        return _current;
    }

    public void Dispose()
    {
        if (planner is IDisposable disposable) disposable.Dispose();
    }
}
