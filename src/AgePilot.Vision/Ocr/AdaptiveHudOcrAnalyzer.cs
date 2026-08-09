using AgePilot.Vision.Geometry;
using AgePilot.Vision.Profiles;

namespace AgePilot.Vision.Ocr;

public sealed class AdaptiveHudOcrAnalyzer(PaddleNumericOcrEngine engine, HudProfile profile)
{
    private static readonly TimeSpan ForcedFieldRefresh = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PauseCheckInterval = TimeSpan.FromSeconds(1);
    private readonly HudField[] _fields = Enum.GetValues<HudField>();
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    public int LastRecognizedRegionCount { get; private set; }

    public HudOcrResult AnalyzeFrame(ReadOnlyMemory<byte> bgraPixels, int width, int height, DateTimeOffset capturedAt)
    {
        var requests = new List<RegionRequest>();
        foreach (var field in _fields)
        {
            AddIfChanged(field.ToString(), profile.Regions[field].ToPixels(width, height), false);
        }
        if (profile.AgeRegion is { } ageRegion) AddIfChanged("age", ageRegion.ToPixels(width, height), false);
        if (profile.PauseMenuRegion is { } pauseRegion) AddIfChanged("pause", pauseRegion.ToPixels(width, height), true);

        if (requests.Count > 0)
        {
            var observations = engine.RecognizeFrame(bgraPixels, width, height, requests.Select(item => item.Region).ToArray());
            for (var index = 0; index < requests.Count; index++)
            {
                var request = requests[index];
                _cache[request.Key] = new CacheEntry(request.Fingerprint, observations[index], capturedAt);
            }
        }
        LastRecognizedRegionCount = requests.Count;

        var fields = _fields.ToDictionary(field => field, field => _cache[field.ToString()].Observation);
        var population = PopulationTextParser.Parse(fields[HudField.Population].RawText);
        var age = _cache.TryGetValue("age", out var ageEntry) ? ageEntry.Observation : null;
        var pause = _cache.TryGetValue("pause", out var pauseEntry) ? pauseEntry.Observation : null;
        return new HudOcrResult(fields, population, GameAgeTextParser.Parse(age?.RawText), age,
            PauseMenuTextParser.IsVisible(pause?.RawText), pause);

        void AddIfChanged(string key, PixelRect region, bool intervalOnly)
        {
            var fingerprint = Fingerprint(bgraPixels.Span, width, region);
            if (!_cache.TryGetValue(key, out var entry) ||
                (intervalOnly ? capturedAt - entry.RecognizedAt >= PauseCheckInterval :
                    fingerprint != entry.Fingerprint || capturedAt - entry.RecognizedAt >= ForcedFieldRefresh))
            {
                requests.Add(new RegionRequest(key, region, fingerprint));
            }
        }
    }

    private static ulong Fingerprint(ReadOnlySpan<byte> pixels, int frameWidth, PixelRect region)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offset;
        var stride = checked(frameWidth * 4);
        for (var y = region.Y; y < region.Bottom; y += 2)
        {
            var row = y * stride;
            for (var x = region.X; x < region.Right; x += 2)
            {
                var pixel = row + x * 4;
                hash = (hash ^ pixels[pixel]) * prime;
                hash = (hash ^ pixels[pixel + 1]) * prime;
                hash = (hash ^ pixels[pixel + 2]) * prime;
            }
        }
        return hash;
    }

    private sealed record CacheEntry(ulong Fingerprint, OcrResult Observation, DateTimeOffset RecognizedAt);
    private sealed record RegionRequest(string Key, PixelRect Region, ulong Fingerprint);
}
