using AgePilot.Core.Configuration;
using AgePilot.Core.Planning;
using AgePilot.Vision.Profiles;

namespace AgePilot.Vision.Images;

public sealed class VisualPromptComposer
{
    private readonly VlmPipelinePreset _preset;
    private readonly HudProfile _profile;
    private readonly PanelHashTracker _panel;
    private readonly Func<DateTimeOffset> _clock;

    public VisualPromptComposer(VlmPipelinePreset preset, HudProfile profile, Func<DateTimeOffset>? clock = null)
    {
        _preset = preset;
        _profile = profile;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        if (profile.BattlefieldRegion is null || profile.CommandPanelRegion is null || profile.MinimapRegion is null)
            throw new InvalidDataException("VLM pipeline 需要 battlefield、command panel 與 minimap ROI。");
        _panel = new PanelHashTracker(preset.PanelDirtyHammingThreshold, preset.PanelCandidateTolerance,
            TimeSpan.FromMilliseconds(preset.PanelStableMilliseconds), preset.PanelMinimumStableSamples);
    }

    public VlmPipelinePreset Preset => _preset;
    public PanelHashSnapshot PanelState => _panel.Snapshot;

    public void ObserveFrame(ReadOnlySpan<byte> bgra, int width, int height, DateTimeOffset at)
    {
        if (_preset.Composition == VlmImageComposition.EventPanel)
            _panel.Observe(bgra, width, height, _profile.CommandPanelRegion!.Value, at);
    }

    public void ObservePanelHash(ulong hash, DateTimeOffset at)
    {
        if (_preset.Composition == VlmImageComposition.EventPanel) _panel.ObserveHash(hash, at);
    }

    public IVisualRequestLease Compose(
        ReadOnlySpan<byte> bgra,
        int width,
        int height,
        VisualRequestContext context,
        string uiLayout,
        string? previousAction,
        string? previousResult)
    {
        var reasons = PanelReasons(context);
        var includePanel = _preset.Composition is VlmImageComposition.LegacyThreeImages or VlmImageComposition.BattlefieldThreeImages
            || reasons.Count > 0;
        var panelHash = includePanel ? _panel.Snapshot.CurrentRawHash : null;
        if (includePanel && panelHash is { } attempted)
            _panel.MarkAttempted(attempted, context.At);

        var build = VisualPromptImageEncoder.Compose(bgra, width, height, _preset,
            _profile.BattlefieldRegion!.Value, _profile.CommandPanelRegion!.Value, _profile.MinimapRegion!.Value,
            includePanel, reasons);
        var telemetry = new VisualCompositionTelemetry(_preset.Id, _preset.Revision,
            _panel.Snapshot.HashMilliseconds, build.CropMilliseconds, build.ResizeMilliseconds,
            build.JpegEncodeMilliseconds, reasons);
        var observation = new VisualObservation(width, height, build.Images, uiLayout,
            previousAction, previousResult, telemetry);
        return new Lease(observation, panelHash, this);
    }

    private List<string> PanelReasons(VisualRequestContext context)
    {
        if (_preset.Composition != VlmImageComposition.EventPanel) return [];
        var result = new List<string>();
        var state = _panel.Snapshot;
        if (state.LastAcceptedPanelHash is null) result.Add("bootstrap");
        if (state.PanelDirty) result.Add("dirty");
        if (context.Events.Any(IsPanelRelevant)) result.Add("action-context");
        if (_panel.AcceptedTtlExpired(context.At, TimeSpan.FromSeconds(_preset.PanelAcceptedTtlSeconds))) result.Add("ttl");
        return result;
    }

    private static bool IsPanelRelevant(PlanningEvent item) =>
        item.Kind is "visual_action_sent" or "visual_action_confirmed" or "visual_action_blocked" ||
        item.Detail.Contains("村民", StringComparison.Ordinal) ||
        item.Detail.Contains("建造", StringComparison.Ordinal) ||
        item.Detail.Contains("科技", StringComparison.Ordinal) ||
        item.Detail.Contains("時代", StringComparison.Ordinal);

    private void Complete(ulong? panelHash, bool accepted)
    {
        if (accepted && panelHash is { } hash) _panel.MarkAccepted(hash, _clock());
    }

    private sealed class Lease(VisualObservation observation, ulong? panelHash, VisualPromptComposer owner)
        : IVisualRequestLease
    {
        private int _completed;
        public VisualObservation Observation { get; } = observation;

        public void Complete(bool accepted)
        {
            if (Interlocked.Exchange(ref _completed, 1) == 0) owner.Complete(panelHash, accepted);
        }
    }
}
