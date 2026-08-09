using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AgePilot.Core.History;
using AgePilot.Core.Rules;
using AgePilot.Vision.Observations;
using AgePilot.Vision.Ocr;
using AgePilot.Vision.Profiles;
using AgePilot.Vision.Images;

namespace AgePilot.App;

public static class ReplayBenchmark
{
    public static ReplayBenchmarkReport Run(string manifestPath, int cycles)
    {
        if (cycles is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(cycles));
        manifestPath = Path.GetFullPath(manifestPath);
        var manifestDirectory = Path.GetDirectoryName(manifestPath)!;
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var samples = document.RootElement.GetProperty("samples").EnumerateArray()
            .Select(sample =>
            {
                var imagePath = Path.GetFullPath(sample.GetProperty("image").GetString()!, manifestDirectory);
                return new ReplaySample(
                    sample.GetProperty("id").GetString()!,
                    BgraImageLoader.Load(imagePath),
                    Path.GetFullPath(sample.GetProperty("profile").GetString()!, manifestDirectory),
                    sample.TryGetProperty("paused", out var paused) && paused.GetBoolean());
            })
            .ToArray();
        if (samples.Length == 0) throw new InvalidDataException("Replay manifest contains no samples.");

        using var engine = new PaddleNumericOcrEngine();
        var analyzers = samples.Select(sample => sample.ProfilePath).Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(path => path, path => new AdaptiveHudOcrAnalyzer(engine, HudProfileLoader.Load(path)), StringComparer.OrdinalIgnoreCase);
        var estimator = new TemporalGameStateEstimator();
        var history = new GameHistory();
        var coach = new CoachEngine([
            new PopulationCriticalRule(), new PopulationLowRule(), new WoodOverflowRule(),
            new GoldLowForCastleRule(), new CastleReadyRule(), new ImperialReadyRule(), new ResourceOverflowRule(),
        ]);
        var process = Process.GetCurrentProcess();
        var cpuStarted = process.TotalProcessorTime;
        var wall = Stopwatch.StartNew();
        var latencies = new List<double>();
        var failures = new List<string>();
        var recommendationFrames = 0;
        var suppressedPausedFrames = 0;
        var recognizedRegions = 0;
        var frameIndex = 0;
        long peakWorkingSet = process.WorkingSet64;

        for (var cycle = 0; cycle < cycles; cycle++)
        {
            foreach (var sample in samples)
            {
                try
                {
                    var frameTimer = Stopwatch.StartNew();
                    var analyzer = analyzers[sample.ProfilePath];
                    var capturedAt = DateTimeOffset.UtcNow.AddMilliseconds(frameIndex++ * 500d);
                    var result = analyzer.AnalyzeFrame(sample.Frame.Pixels, sample.Frame.Width, sample.Frame.Height, capturedAt);
                    recognizedRegions += analyzer.LastRecognizedRegionCount;
                    frameTimer.Stop();
                    latencies.Add(frameTimer.Elapsed.TotalMilliseconds);
                    if (sample.Paused || result.IsPauseMenuVisible)
                    {
                        suppressedPausedFrames++;
                    }
                    else
                    {
                        var now = DateTimeOffset.UtcNow;
                        var state = estimator.Update(result, now);
                        history.Add(state, now);
                        if (coach.Evaluate(state, history).Count > 0) recommendationFrames++;
                    }
                    process.Refresh();
                    peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
                }
                catch (Exception exception)
                {
                    failures.Add($"cycle={cycle} sample={sample.Id}: {exception.Message}");
                }
            }
        }

        wall.Stop();
        process.Refresh();
        var cpuUsed = process.TotalProcessorTime - cpuStarted;
        var cpuPercent = wall.Elapsed.TotalMilliseconds <= 0 ? 0 :
            cpuUsed.TotalMilliseconds / wall.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100;
        var ordered = latencies.Order().ToArray();
        return new ReplayBenchmarkReport(
            1, DateTimeOffset.UtcNow, cycles, cycles * samples.Length, failures.Count,
            wall.Elapsed.TotalSeconds, cpuPercent, peakWorkingSet / 1024d / 1024d,
            ordered.Length == 0 ? 0 : ordered.Average(), Percentile(ordered, 0.95),
            recommendationFrames, suppressedPausedFrames,
            latencies.Count == 0 ? 0 : (double)recognizedRegions / latencies.Count, failures);
    }

    public static void WriteJson(ReplayBenchmarkReport report, string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static double Percentile(double[] sorted, double percentile) => sorted.Length == 0 ? 0 :
        sorted[Math.Clamp((int)Math.Ceiling(percentile * sorted.Length) - 1, 0, sorted.Length - 1)];

    private sealed record ReplaySample(string Id, BgraImage Frame, string ProfilePath, bool Paused);
}

public sealed record ReplayBenchmarkReport(
    int SchemaVersion,
    DateTimeOffset GeneratedAt,
    int Cycles,
    int Frames,
    int Failures,
    double DurationSeconds,
    double AverageCpuPercent,
    double PeakWorkingSetMegabytes,
    double AverageOcrLatencyMilliseconds,
    double P95OcrLatencyMilliseconds,
    int RecommendationFrames,
    int SuppressedPausedFrames,
    double AverageRecognizedRegionsPerFrame,
    IReadOnlyList<string> FailureDetails);
