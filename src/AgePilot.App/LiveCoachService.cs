using AgePilot.Core;
using AgePilot.Core.History;
using AgePilot.Core.Recommendations;
using AgePilot.Core.Rules;
using AgePilot.Vision.Capture;
using AgePilot.Vision.Observations;
using AgePilot.Vision.Ocr;
using AgePilot.Vision.Profiles;

namespace AgePilot.App;

public sealed class LiveCoachService(string profilePath) : IDisposable
{
    private readonly WindowsGameWindowLocator _locator = new();
    private readonly WindowsGdiFrameCapture _capture = new();
    private readonly PaddleNumericOcrEngine _ocr = new();
    private readonly TemporalGameStateEstimator _estimator = new();
    private readonly GameHistory _history = new();
    private readonly CoachEngine _coach = new(new ICoachRule[]
    {
        new PopulationCriticalRule(),
        new PopulationLowRule(),
        new WoodOverflowRule(),
    });
    private readonly HudProfile _profile = HudProfileLoader.Load(profilePath);

    public async Task RunAsync(
        Func<LiveCoachUpdate, Task> onUpdate,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var cycleStartedAt = DateTimeOffset.UtcNow;
            try
            {
                var window = _locator.Find();
                if (window is null)
                {
                    await onUpdate(LiveCoachUpdate.Disconnected("等待 AOE2 DE 遊戲視窗…"));
                }
                else
                {
                    var frame = await _capture.CaptureAsync(window, cancellationToken);
                    var raw = new HudOcrAnalyzer(_ocr)
                        .AnalyzeFrame(frame.BgraPixels, frame.Width, frame.Height, _profile);
                    var state = _estimator.Update(raw, frame.CapturedAt);
                    _history.Add(state, frame.CapturedAt);
                    var recommendations = _coach.Evaluate(state, _history);
                    await onUpdate(new LiveCoachUpdate(true, "監測中", state, recommendations));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                await onUpdate(LiveCoachUpdate.Disconnected($"辨識暫停：{exception.Message}"));
            }

            var remaining = TimeSpan.FromMilliseconds(500) - (DateTimeOffset.UtcNow - cycleStartedAt);
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, cancellationToken);
            }
        }
    }

    public void Dispose() => _ocr.Dispose();
}

public sealed record LiveCoachUpdate(
    bool IsConnected,
    string Status,
    GameState? State,
    IReadOnlyList<Recommendation> Recommendations)
{
    public static LiveCoachUpdate Disconnected(string status) => new(false, status, null, []);
}
