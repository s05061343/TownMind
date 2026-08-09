using AgePilot.Core.Automation;

namespace AgePilot.Vision.World;

public sealed class GenericWorldAnalyzer
{
    private const int SampleStep = 8;

    public WorldObservation Analyze(byte[] bgra, int width, int height)
    {
        if (bgra.Length != checked(width * height * 4)) throw new ArgumentException("BGRA frame size mismatch.", nameof(bgra));
        var minY = Math.Max(80, height / 16);
        var maxY = Math.Min(height - 250, height * 4 / 5);
        var buckets = Enum.GetValues<WorldTargetKind>()
            .ToDictionary(kind => kind, _ => new List<(double X, double Y, double Score)>());

        for (var y = minY; y < maxY; y += SampleStep)
        {
            for (var x = 20; x < width - 20; x += SampleStep)
            {
                var index = (y * width + x) * 4;
                var b = bgra[index];
                var g = bgra[index + 1];
                var r = bgra[index + 2];
                var kind = Classify(r, g, b);
                if (kind is not null)
                    buckets[kind.Value].Add(((double)x / width, (double)y / height, 1));
            }
        }

        AddOpenBuildCandidates(bgra, width, height, minY, maxY, buckets[WorldTargetKind.OpenBuildArea]);
        var targets = buckets.SelectMany(pair => Cluster(pair.Key, pair.Value)).ToArray();
        var confidence = targets.Count(target => target.Confidence >= 0.55) / 5d;
        return new WorldObservation(width, height, targets, Math.Clamp(confidence, 0, 1));
    }

    private static WorldTargetKind? Classify(byte r, byte g, byte b)
    {
        if (g > 55 && g > r * 1.18 && g > b * 1.12) return WorldTargetKind.Wood;
        if (r > 145 && g > 105 && b < 105 && r - b > 55) return WorldTargetKind.Gold;
        if (r > 105 && b > 65 && r > g * 1.18 && b > g * 0.8) return WorldTargetKind.Food;
        if (r is > 75 and < 175 && Math.Abs(r - g) < 14 && Math.Abs(g - b) < 14) return WorldTargetKind.Stone;
        return null;
    }

    private static IEnumerable<WorldTarget> Cluster(WorldTargetKind kind, List<(double X, double Y, double Score)> samples)
    {
        if (kind == WorldTargetKind.OpenBuildArea)
        {
            foreach (var sample in samples.OrderByDescending(point => point.Score).Take(6))
                yield return new WorldTarget(kind, sample.X, sample.Y, sample.Score);
            yield break;
        }
        if (samples.Count < 8) yield break;
        foreach (var group in samples.GroupBy(point => ((int)(point.X * 20), (int)(point.Y * 12)))
                     .OrderByDescending(group => group.Count()).Take(5))
        {
            var count = group.Count();
            if (count < 4) continue;
            yield return new WorldTarget(kind, group.Average(p => p.X), group.Average(p => p.Y), Math.Clamp(count / 40d, 0.35, 0.9));
        }
    }

    private static void AddOpenBuildCandidates(
        byte[] pixels, int width, int height, int minY, int maxY,
        List<(double X, double Y, double Score)> output)
    {
        var centerX = width / 2;
        var centerY = (minY + maxY) / 2;
        foreach (var (dx, dy) in new[] { (-360, -220), (360, -220), (-420, 80), (420, 80), (-260, 260), (260, 260) })
        {
            var x = Math.Clamp(centerX + dx * width / 2560, 80, width - 80);
            var y = Math.Clamp(centerY + dy * height / 1440, minY + 60, maxY - 60);
            var values = new List<int>();
            for (var sy = y - 32; sy <= y + 32; sy += 16)
            for (var sx = x - 32; sx <= x + 32; sx += 16)
            {
                var i = (sy * width + sx) * 4;
                values.Add((pixels[i] + pixels[i + 1] + pixels[i + 2]) / 3);
            }
            var average = values.Average();
            var deviation = values.Average(value => Math.Abs(value - average));
            if (average > 45 && deviation < 60)
                output.Add(((double)x / width, (double)y / height, Math.Clamp(1 - deviation / 80d, 0.55, 0.85)));
        }
    }
}
