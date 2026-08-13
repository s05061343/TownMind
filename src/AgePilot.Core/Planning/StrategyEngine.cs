using AgePilot.Core.History;

namespace AgePilot.Core.Planning;

public sealed class StrategyEngine : IDisposable
{
    private readonly IStrategicPlanner _planner;
    private readonly StrategyDirective _directive;
    private GamePlan? _current;
    private Task<PlanningResult>? _pending;
    private PlanUpdateScope? _pendingScope;
    private PlanUpdateScope? _requestedScope = PlanUpdateScope.Major;
    private PlanUpdateScope? _retryScope;
    private DateTimeOffset _retryNotBefore = DateTimeOffset.MaxValue;
    private GameAge? _lastAge;
    private bool? _lastPopulationCapped;
    private MapArchetype _lastMap = MapArchetype.Unknown;
    private readonly Queue<PlanningEvent> _executionEvents = new();

    public StrategyEngine(IStrategicPlanner planner, StrategyDirective? directive = null)
    {
        _planner = planner;
        _directive = directive ?? new StrategyDirective("穩定發展經濟並升時代", GameAge.Castle);
    }

    public PlannerRuntimeStatus RuntimeStatus => _planner is IPlannerRuntimeStatusSource source
        ? source.RuntimeStatus
        : PlannerRuntimeStatus.NotConfigured("規劃器未提供 runtime 狀態");

    public Task<(GamePlan? Plan, string? Status)> UpdateAsync(
        GameState state, GameHistory history, MapContext? map, DateTimeOffset now, CancellationToken cancellationToken,
        VisualObservation? visual = null)
    {
        if (_pending?.IsCompleted == true)
        {
            var result = _pending.GetAwaiter().GetResult();
            _pending = null;
            if (result.Success)
            {
                var completedScope = _pendingScope ?? PlanUpdateScope.Major;
                _current = Merge(_current, result.Plan!, completedScope);
                if ((int)result.Plan!.RequestedUpdateScope > (int)completedScope)
                    Request(result.Plan.RequestedUpdateScope);
                _retryScope = null;
                _retryNotBefore = DateTimeOffset.MaxValue;
            }
            else
            {
                if (_current is not null) _current = _current with { ReusedAfterPlanningFailure = true };
                _retryScope = _pendingScope ?? PlanUpdateScope.Minor;
                _retryNotBefore = now.AddSeconds(10);
            }
            _pendingScope = null;
        }

        var populationCapped = state.Population?.IsUsable == true && state.PopulationCap?.IsUsable == true &&
                               state.Population.Value >= state.PopulationCap.Value;
        var ageChanged = state.Age != _lastAge;
        var populationChanged = populationCapped != _lastPopulationCapped;
        var mapChanged = map?.IsUsable == true && map.Archetype != _lastMap;
        _lastAge = state.Age; _lastPopulationCapped = populationCapped;
        if (map?.IsUsable == true) _lastMap = map.Archetype;

        if (ageChanged) Request(PlanUpdateScope.Major);
        else if (mapChanged) Request(PlanUpdateScope.Medium);
        else if (populationChanged) Request(PlanUpdateScope.Minor);
        if (_pending is null && _retryScope is { } retryScope && now >= _retryNotBefore)
        {
            _retryScope = null;
            Request(retryScope);
        }
        if (_pending is null && _retryScope is null && _current is not null && _current.ExpiresAt <= now) Request(PlanUpdateScope.Minor);

        if (_pending is null && _requestedScope is { } scope)
        {
            _requestedScope = null;
            _pendingScope = scope;
            var events = _executionEvents.ToList();
            _executionEvents.Clear();
            if (ageChanged) events.Add(new PlanningEvent("age_changed", "時代改變", now, PlanUpdateScope.Major));
            else if (mapChanged) events.Add(new PlanningEvent("map_changed", "可用地形判斷改變", now, PlanUpdateScope.Medium));
            else if (populationChanged) events.Add(new PlanningEvent("population_gate_changed", "人口上限狀態改變", now));
            var context = new SituationContext(state, GameHistorySummarizer.Summarize(history, TimeSpan.FromSeconds(120), now),
                map?.IsUsable == true ? map : null, _current, events, now, visual, _directive, scope);
            _pending = _planner.PlanAsync(context, cancellationToken);
        }

        var current = Current(now);
        return Task.FromResult((current, _pending is not null ? "背景規劃中" : current is null ? "規劃暫時不可用" : null));
    }

    public (GamePlan? Plan, string? Status) Pause(DateTimeOffset now, string status) => (null, status);

    public void ReportExecutionEvent(PlanningEvent executionEvent)
    {
        _executionEvents.Enqueue(executionEvent);
        if (executionEvent.Kind is not "visual_action_sent") Request(executionEvent.Scope);
    }

    private GamePlan? Current(DateTimeOffset now)
    {
        return _current;
    }

    private void Request(PlanUpdateScope scope)
    {
        if (_current is null) scope = PlanUpdateScope.Major;
        if (_requestedScope is null || (int)scope > (int)_requestedScope.Value) _requestedScope = scope;
    }

    private static GamePlan Merge(GamePlan? previous, GamePlan next, PlanUpdateScope scope)
    {
        if (previous is null || scope == PlanUpdateScope.Major) return next;
        return scope == PlanUpdateScope.Medium
            ? next with { MajorDecision = previous.MajorDecision, Revision = previous.Revision + 1 }
            : next with
            {
                MajorDecision = previous.MajorDecision,
                MediumDecision = previous.MediumDecision,
                Revision = previous.Revision + 1,
            };
    }

    public void Dispose()
    {
        if (_planner is IDisposable disposable) disposable.Dispose();
    }
}
