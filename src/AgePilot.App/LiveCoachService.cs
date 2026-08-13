using AgePilot.Core;
using AgePilot.Core.History;
using AgePilot.Core.Recommendations;
using AgePilot.Core.Rules;
using AgePilot.Core.Persistence;
using AgePilot.Vision.Capture;
using AgePilot.Vision.Observations;
using AgePilot.Vision.Ocr;
using AgePilot.Vision.Profiles;
using AgePilot.Infrastructure.Diagnostics;
using AgePilot.Core.Configuration;
using AgePilot.Core.Planning;
using AgePilot.Infrastructure.Planning;
using AgePilot.Vision.Images;
using AgePilot.Vision.Geometry;
using AgePilot.Vision.World;

namespace AgePilot.App;

public sealed class LiveCoachService(
    string profilePath,
    AppSettings settings,
    int scanIntervalMilliseconds = 500,
    ISessionRepository? sessionRepository = null,
    LocalJsonLineLogger? logger = null) : IDisposable
{
    private readonly WindowsGameWindowLocator _locator = new();
    private readonly WindowsGdiFrameCapture _capture = new();
    private readonly PaddleNumericOcrEngine _ocr = new();
    private readonly TemporalGameStateEstimator _estimator = new();
    private readonly GameHistory _history = new();
    private readonly StrategyEngine _strategy = new(new LlamaServerPlanner(settings, logger),
        new StrategyDirective("穩定發展經濟並升至玩家指定時代", settings.TargetAge));
    private readonly RecommendationCoordinator _recommendationCoordinator = new();
    private readonly GameLifecycleTracker _lifecycle = new();
    private readonly MinimapAnalyzer _minimapAnalyzer = new();
    private readonly HudProfile _profile = HudProfileLoader.Load(profilePath);
    private readonly ISessionRepository? _sessions = sessionRepository;
    private readonly HashSet<string> _activeRecommendationIds = [];
    private AdaptiveHudOcrAnalyzer? _adaptiveOcr;
    private long? _sessionId;
    private DateTimeOffset _lastSnapshotAt = DateTimeOffset.MinValue;
    private GameLifecycleState? _lastLoggedLifecycle;
    private string _lastLoggedRecommendations = string.Empty;
    private string? _previousVisualAction;
    private string? _previousVisualResult;

    public async Task RunAsync(
        Func<LiveCoachUpdate, Task> onUpdate,
        CancellationToken cancellationToken)
    {
        if (_sessions is not null)
        {
            await _sessions.InitializeAsync(cancellationToken);
        }
        _adaptiveOcr ??= new AdaptiveHudOcrAnalyzer(_ocr, _profile);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var cycleStartedAt = DateTimeOffset.UtcNow;
                try
                {
                    var window = _locator.Find();
                    if (window is null)
                    {
                        await EndSessionAsync(CancellationToken.None);
                        var lifecycle = _lifecycle.ObserveWindow(false);
                        LogLifecycle(lifecycle);
                        await onUpdate(LiveCoachUpdate.Disconnected(
                            lifecycle,
                            lifecycle == GameLifecycleState.GameEnded ? "對局已結束，等待下一局…" : "等待 AOE2 DE 遊戲視窗…"));
                    }
                    else
                    {
                        _ = _lifecycle.ObserveWindow(true);
                        await EnsureSessionAsync(cycleStartedAt, cancellationToken);
                        var frame = await _capture.CaptureAsync(window, cancellationToken);
                        var raw = _adaptiveOcr.AnalyzeFrame(
                            frame.BgraPixels, frame.Width, frame.Height, frame.CapturedAt);
                        if (raw.IsPauseMenuVisible)
                        {
                            var paused = _lifecycle.ObserveFrame(true, 0);
                            LogLifecycle(paused);
                            await onUpdate(new LiveCoachUpdate(
                                true, "遊戲已暫停 · 建議暫停", paused, null, [], 0, 6,
                                DateTimeOffset.UtcNow - cycleStartedAt));
                            await Task.Delay(TimeSpan.FromMilliseconds(scanIntervalMilliseconds), cancellationToken);
                            continue;
                        }
                        var state = _estimator.Update(raw, frame.CapturedAt);
                        var map = _minimapAnalyzer.Analyze(frame.BgraPixels.Span, frame.Width, frame.Height, _profile.MinimapRegion, frame.CapturedAt);
                        var visual = new VisualObservation(frame.Width, frame.Height,
                            VisualPromptImageEncoder.Encode(frame.BgraPixels.Span, frame.Width, frame.Height,
                                _profile.CommandPanelRegion ?? new NormalizedRect(0, 0.66, 0.47, 0.34),
                                _profile.MinimapRegion ?? new NormalizedRect(0.80, 0.67, 0.20, 0.33)),
                            $"panorama=完整遊戲視窗；資源數值已由 OCR 以文字提供，不再附圖；command_panel=左下指令；minimap=右下小地圖；所有遊戲輸入僅限滑鼠；CommandGrid={_profile.CommandGridRows}x{_profile.CommandGridColumns}；世界座標以 panorama normalized [0,1] 表示",
                            _previousVisualAction, _previousVisualResult);
                        _history.Add(state, frame.CapturedAt);
                        var health = CalculateVisionHealth(state);
                        var lifecycle = _lifecycle.ObserveFrame(false, 6 - health.UnavailableFields);
                        var planning = lifecycle == GameLifecycleState.GameActive
                            ? await _strategy.UpdateAsync(state, _history, map, frame.CapturedAt, cancellationToken, visual)
                            : _strategy.Pause(frame.CapturedAt, "等待可靠的 Active 遊戲狀態");
                        var activeRecommendations = GamePlanRecommendationAdapter.Convert(planning.Plan);
                        var visibleRecommendations = _recommendationCoordinator.Apply(activeRecommendations);
                        await PersistCycleAsync(frame.CapturedAt, state, activeRecommendations, cancellationToken);
                        LogLifecycle(lifecycle);
                        LogRecommendations(visibleRecommendations);
                        var status = lifecycle == GameLifecycleState.GameLoading
                            ? "遊戲載入中 · 等待可靠 HUD"
                            : $"監測中 · {FormatAge(state.Age)}";
                        await onUpdate(new LiveCoachUpdate(true, status, lifecycle, state,
                            lifecycle == GameLifecycleState.GameActive ? visibleRecommendations : [],
                            health.Confidence, health.UnavailableFields, DateTimeOffset.UtcNow - cycleStartedAt,
                            map, planning.Plan, planning.Status, _strategy.RuntimeStatus));
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger?.Write("vision.failure", new { type = exception.GetType().Name, exception.Message });
                    await onUpdate(LiveCoachUpdate.Disconnected(
                        _lifecycle.ObserveFailure(), $"遊戲暫時無法讀取：{exception.Message}"));
                }

                var remaining = TimeSpan.FromMilliseconds(scanIntervalMilliseconds) - (DateTimeOffset.UtcNow - cycleStartedAt);
                if (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(remaining, cancellationToken);
                }
            }
        }
        finally
        {
            await EndSessionAsync(CancellationToken.None);
        }
    }

    private async Task EnsureSessionAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (_sessions is null || _sessionId is not null) return;
        _sessionId = await _sessions.StartSessionAsync(_profile.Id, now, cancellationToken);
        _lastSnapshotAt = DateTimeOffset.MinValue;
        _activeRecommendationIds.Clear();
    }

    private async Task PersistCycleAsync(
        DateTimeOffset capturedAt,
        GameState state,
        IReadOnlyList<Recommendation> recommendations,
        CancellationToken cancellationToken)
    {
        if (_sessions is null || _sessionId is not { } sessionId) return;
        if (capturedAt - _lastSnapshotAt >= TimeSpan.FromSeconds(5))
        {
            await _sessions.AddSnapshotAsync(sessionId, capturedAt, state, cancellationToken);
            _lastSnapshotAt = capturedAt;
        }

        var currentIds = recommendations.Select(item => item.Id).ToHashSet();
        foreach (var recommendation in recommendations.Where(item => !_activeRecommendationIds.Contains(item.Id)))
        {
            await _sessions.AddRecommendationAsync(sessionId, capturedAt, recommendation, cancellationToken);
        }
        _activeRecommendationIds.Clear();
        _activeRecommendationIds.UnionWith(currentIds);
    }

    private async Task EndSessionAsync(CancellationToken cancellationToken)
    {
        if (_sessions is null || _sessionId is not { } sessionId) return;
        _sessionId = null;
        _activeRecommendationIds.Clear();
        await _sessions.EndSessionAsync(sessionId, DateTimeOffset.UtcNow, cancellationToken);
    }

    private void LogLifecycle(GameLifecycleState lifecycle)
    {
        if (_lastLoggedLifecycle == lifecycle) return;
        _lastLoggedLifecycle = lifecycle;
        logger?.Write("game.lifecycle", new { state = lifecycle.ToString() });
    }

    private void LogRecommendations(IReadOnlyList<Recommendation> recommendations)
    {
        var ids = string.Join(",", recommendations.Select(item => item.Id));
        if (ids == _lastLoggedRecommendations) return;
        _lastLoggedRecommendations = ids;
        logger?.Write("recommendations.changed", new { ids = recommendations.Select(item => item.Id).ToArray() });
    }

    private static string FormatAge(GameAge? age) => age switch
    {
        GameAge.Dark => "黑暗時代",
        GameAge.Feudal => "封建時代",
        GameAge.Castle => "城堡時代",
        GameAge.Imperial => "帝王時代",
        _ => "時代確認中",
    };

    private static (double Confidence, int UnavailableFields) CalculateVisionHealth(GameState state)
    {
        var fields = new[] { state.Food, state.Wood, state.Gold, state.Stone, state.Population, state.PopulationCap };
        var usable = fields.Where(field => field?.IsUsable == true).ToArray();
        return (usable.Length == 0 ? 0 : usable.Average(field => field!.Confidence), fields.Length - usable.Length);
    }

    public void Dispose()
    {
        _ocr.Dispose();
        _strategy.Dispose();
    }

    public void DismissRecommendation(string recommendationId) =>
        _recommendationCoordinator.Dismiss(recommendationId);

    public void ReportExecutionEvent(PlanningEvent executionEvent)
    {
        if (executionEvent.Kind == "visual_action_sent")
        {
            _previousVisualAction = executionEvent.Detail;
            _previousVisualResult = null;
        }
        else if (executionEvent.Kind is not ("visual_action_recheck_due" or "visual_wait_elapsed"))
        {
            _previousVisualResult = executionEvent.Kind;
        }
        _strategy.ReportExecutionEvent(executionEvent);
    }
}

public sealed record LiveCoachUpdate(
    bool IsConnected,
    string Status,
    GameLifecycleState Lifecycle,
    GameState? State,
    IReadOnlyList<Recommendation> Recommendations,
    double VisionConfidence,
    int UnavailableFields,
    TimeSpan AnalysisLatency,
    MapContext? Map = null,
    GamePlan? Plan = null,
    string? PlanningStatus = null,
    PlannerRuntimeStatus? LlmStatus = null)
{
    public static LiveCoachUpdate Disconnected(GameLifecycleState lifecycle, string status) =>
        new(false, status, lifecycle, null, [], 0, 6, TimeSpan.Zero);
}
