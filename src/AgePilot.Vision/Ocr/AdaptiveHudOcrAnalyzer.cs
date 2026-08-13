using AgePilot.Vision.Geometry;
using AgePilot.Vision.Profiles;

namespace AgePilot.Vision.Ocr;

public sealed class AdaptiveHudOcrAnalyzer(IFrameOcrEngine engine, HudProfile profile)
{
    private const double ImmediateCacheConfidence = 0.7;
    private const double CandidateConfidence = 0.45;
    private static readonly TimeSpan ForcedFieldRefresh = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PauseCheckInterval = TimeSpan.FromSeconds(1);
    private readonly HudField[] _fields = Enum.GetValues<HudField>();
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CandidateEntry> _candidates = new(StringComparer.Ordinal);

    public int LastRecognizedRegionCount { get; private set; }
    public IReadOnlyList<OcrCacheOutcome> LastCacheOutcomes { get; private set; } = [];

    public HudOcrResult AnalyzeFrame(ReadOnlyMemory<byte> bgraPixels, int width, int height, DateTimeOffset capturedAt)
    {
        var requests = new List<RegionRequest>();
        var current = new Dictionary<string, OcrResult>(StringComparer.Ordinal);
        foreach (var field in _fields)
            AddIfChanged(field.ToString(), profile.Regions[field].ToPixels(width, height), false);
        if (profile.AgeRegion is { } ageRegion) AddIfChanged("age", ageRegion.ToPixels(width, height), false);
        if (profile.PauseMenuRegion is { } pauseRegion) AddIfChanged("pause", pauseRegion.ToPixels(width, height), true);

        var outcomes = new List<OcrCacheOutcome>();
        if (requests.Count > 0)
        {
            var observations = engine.RecognizeFrame(bgraPixels, width, height, requests.Select(item => item.Region).ToArray());
            for (var index = 0; index < requests.Count; index++)
            {
                var request = requests[index];
                var observation = observations[index];
                if (request.Key == HudField.Population.ToString() &&
                    engine is IPopulationOcrEngine populationEngine &&
                    PopulationTextParser.ParseDetailed(observation.RawText)?.Kind is not PopulationParseKind.LiteralSeparator)
                {
                    observation = populationEngine.RefinePopulation(
                        bgraPixels, width, height, request.Region, observation);
                }
                if (request.Key == HudField.Population.ToString() &&
                    PopulationTextParser.ParseDetailed(observation.RawText)?.Kind is not PopulationParseKind.LiteralSeparator)
                {
                    observation = observation with { Confidence = Math.Min(observation.Confidence, ImmediateCacheConfidence - 0.01) };
                }
                current[request.Key] = observation;
                var identity = CandidateIdentity(request.Key, observation);

                if (request.Key == "pause")
                {
                    _cache[request.Key] = new CacheEntry(request.Fingerprint, observation, capturedAt);
                    outcomes.Add(new(request.Key, observation.RawText, observation.Confidence, true, "pause-check", 1));
                    continue;
                }

                if (identity is null || observation.Confidence < CandidateConfidence)
                {
                    _cache.Remove(request.Key);
                    _candidates.Remove(request.Key);
                    outcomes.Add(new(request.Key, observation.RawText, observation.Confidence, false,
                        identity is null ? "parse-failed" : "confidence-below-45-percent", 0));
                    continue;
                }

                if (observation.Confidence >= ImmediateCacheConfidence)
                {
                    _cache[request.Key] = new CacheEntry(request.Fingerprint, observation, capturedAt);
                    _candidates.Remove(request.Key);
                    outcomes.Add(new(request.Key, observation.RawText, observation.Confidence, true, "high-confidence", 1));
                    continue;
                }

                var count = _candidates.TryGetValue(request.Key, out var candidate) && candidate.Identity == identity
                    ? candidate.Count + 1
                    : 1;
                _candidates[request.Key] = new CandidateEntry(identity, count);
                if (count >= 2)
                {
                    _cache[request.Key] = new CacheEntry(request.Fingerprint, observation, capturedAt);
                    _candidates.Remove(request.Key);
                    outcomes.Add(new(request.Key, observation.RawText, observation.Confidence, true, "two-consistent-candidates", count));
                }
                else
                {
                    _cache.Remove(request.Key);
                    outcomes.Add(new(request.Key, observation.RawText, observation.Confidence, false, "awaiting-second-candidate", count));
                }
            }
        }
        LastRecognizedRegionCount = requests.Count;
        LastCacheOutcomes = outcomes;

        var fields = _fields.ToDictionary(field => field, field => current[field.ToString()]);
        var population = PopulationTextParser.Parse(fields[HudField.Population].RawText);
        var age = current.TryGetValue("age", out var ageObservation) ? ageObservation : null;
        var pause = current.TryGetValue("pause", out var pauseObservation) ? pauseObservation : null;
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
            else
            {
                current[key] = entry.Observation;
            }
        }
    }

    private static string? CandidateIdentity(string key, OcrResult observation)
    {
        if (key == HudField.Population.ToString())
            return PopulationTextParser.Parse(observation.RawText) is { } population
                ? $"{population.Current}/{population.Cap}"
                : null;
        if (key == "age") return GameAgeTextParser.Parse(observation.RawText)?.ToString();
        return observation.Value is { } value ? value.ToString() : null;
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
    private sealed record CandidateEntry(string Identity, int Count);
    private sealed record RegionRequest(string Key, PixelRect Region, ulong Fingerprint);
}

public sealed record OcrCacheOutcome(
    string Key,
    string RawText,
    double Confidence,
    bool Cached,
    string Reason,
    int ConsistentCount);
