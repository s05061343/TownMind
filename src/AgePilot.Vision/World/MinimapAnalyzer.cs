using AgePilot.Core.Observations;
using AgePilot.Core.Planning;
using AgePilot.Vision.Geometry;

namespace AgePilot.Vision.World;

public sealed class MinimapAnalyzer(int requiredConsistentFrames = 3)
{
    private MapArchetype _lastCandidate = MapArchetype.Unknown;
    private int _consistentFrames;

    public MapContext Analyze(ReadOnlySpan<byte> bgra, int width, int height, NormalizedRect? region, DateTimeOffset observedAt)
    {
        if (region is null || bgra.Length != checked(width * height * 4)) return Unavailable(observedAt);
        var rect = region.Value.ToPixels(width, height);
        var water = 0; var forest = 0; var open = 0; var visible = 0; var sampled = 0;
        for (var y = rect.Y; y < rect.Bottom; y += 3)
        for (var x = rect.X; x < rect.Right; x += 3)
        {
            var i = (y * width + x) * 4;
            var b = bgra[i]; var g = bgra[i + 1]; var r = bgra[i + 2];
            sampled++;
            if (r < 22 && g < 22 && b < 22) continue;
            visible++;
            if (b > 65 && b > r * 1.18 && b > g * 1.08) water++;
            else if (g > 45 && g > r * 1.15 && g > b * 1.05) forest++;
            else if (r > 45 && g > 35 && Math.Abs(r - g) < 75) open++;
        }

        if (sampled == 0) return Unavailable(observedAt);
        var coverage = (double)visible / sampled;
        if (coverage < 0.15 || visible == 0) return Unavailable(observedAt);
        var waterRatio = (double)water / visible;
        var forestRatio = (double)forest / visible;
        var openRatio = (double)open / visible;
        var archetype = waterRatio >= 0.48 ? MapArchetype.Island
            : waterRatio >= 0.18 ? MapArchetype.Coastal
            : forestRatio >= 0.38 ? MapArchetype.ClosedLand
            : openRatio >= 0.35 ? MapArchetype.OpenLand
            : MapArchetype.Unknown;

        if (archetype == _lastCandidate) _consistentFrames++; else { _lastCandidate = archetype; _consistentFrames = 1; }
        var confidence = Math.Clamp(Math.Max(waterRatio, Math.Max(forestRatio, openRatio)), 0, 1);
        var confirmed = archetype != MapArchetype.Unknown && _consistentFrames >= requiredConsistentFrames && confidence >= 0.35;
        return new(archetype, waterRatio, forestRatio, openRatio, coverage,
            LandComponentCount: waterRatio > 0.48 ? 2 : 1,
            ChokePointScore: Math.Clamp(forestRatio - openRatio + 0.5, 0, 1),
            Confidence: confidence,
            Status: confirmed ? ObservationStatus.Confirmed : ObservationStatus.Raw,
            observedAt, _consistentFrames);
    }

    private static MapContext Unavailable(DateTimeOffset at) => new(
        MapArchetype.Unknown, 0, 0, 0, 0, 0, 0, 0, ObservationStatus.Unavailable, at, 0);
}
