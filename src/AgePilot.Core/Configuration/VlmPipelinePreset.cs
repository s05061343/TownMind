namespace AgePilot.Core.Configuration;

public enum VlmImageComposition
{
    LegacyThreeImages,
    BattlefieldThreeImages,
    BattlefieldTwoImages,
    EventPanel,
}

public sealed record VlmPipelinePreset(
    string Id,
    int Revision,
    string DisplayName,
    VlmImageComposition Composition,
    int ImageMinTokens,
    int ImageMaxTokens,
    int BattlefieldMaxEdge,
    int PanelDirtyHammingThreshold = 10,
    int PanelCandidateTolerance = 2,
    int PanelStableMilliseconds = 750,
    int PanelMinimumStableSamples = 2,
    int PanelAcceptedTtlSeconds = 15);

/// <summary>
/// Immutable experiment definitions. Changing any behavior requires a new id/revision so reports remain comparable.
/// The production default intentionally remains the legacy pipeline until a candidate passes the documented gate.
/// </summary>
public static class VlmPipelinePresetCatalog
{
    public static readonly VlmPipelinePreset Legacy = new(
        "legacy-3-1024-v1", 1, "Legacy 3 images / 1024", VlmImageComposition.LegacyThreeImages, 256, 1024, 1536);

    private static readonly IReadOnlyDictionary<string, VlmPipelinePreset> Presets = new[]
    {
        Legacy,
        new("battlefield-3-1024-v1", 1, "Battlefield 3 images / 1024", VlmImageComposition.BattlefieldThreeImages, 256, 1024, 1536),
        new("battlefield-2-1024-v1", 1, "Battlefield 2 images / 1024", VlmImageComposition.BattlefieldTwoImages, 256, 1024, 1536),
        new("event-panel-1024-v1", 1, "Event panel / 1024", VlmImageComposition.EventPanel, 256, 1024, 1536),
        new("event-panel-640-v1", 1, "Event panel / 640", VlmImageComposition.EventPanel, 256, 640, 1536),
        new("event-panel-512-v1", 1, "Event panel / 512", VlmImageComposition.EventPanel, 256, 512, 1536),
        new("event-panel-edge1536-v1", 1, "Event panel / edge 1536", VlmImageComposition.EventPanel, 256, 640, 1536),
        new("event-panel-edge1280-v1", 1, "Event panel / edge 1280", VlmImageComposition.EventPanel, 256, 640, 1280),
        new("event-panel-edge1024-v1", 1, "Event panel / edge 1024", VlmImageComposition.EventPanel, 256, 640, 1024),
    }.ToDictionary(item => item.Id, StringComparer.Ordinal);

    public static IReadOnlyCollection<VlmPipelinePreset> All { get; } = Presets.Values.ToArray();

    public static VlmPipelinePreset Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || !Presets.TryGetValue(id, out var preset))
            throw new InvalidDataException($"未知或未驗證的 VLM pipeline preset：{id}");
        return preset;
    }
}
