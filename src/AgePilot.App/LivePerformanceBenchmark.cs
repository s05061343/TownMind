using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AgePilot.Vision.Capture;
using AgePilot.Vision.Ocr;
using AgePilot.Vision.Profiles;

namespace AgePilot.App;

public static class LivePerformanceBenchmark
{
    public static async Task<LivePerformanceReport> RunAsync(string profilePath, int durationSeconds, CancellationToken cancellationToken)
    {
        if (durationSeconds is < 10 or > 3600) throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        var locator = new WindowsGameWindowLocator();
        var window = locator.Find() ?? throw new InvalidOperationException("AOE2 DE window not found.");
        var capture = new WindowsGdiFrameCapture();
        using var engine = new PaddleNumericOcrEngine();
        var analyzer = new AdaptiveHudOcrAnalyzer(engine, HudProfileLoader.Load(profilePath));
        var process = Process.GetCurrentProcess();
        var cpuStarted = process.TotalProcessorTime;
        var wall = Stopwatch.StartNew();
        var latencies = new List<double>();
        var captureLatencies = new List<double>();
        var failures = new List<string>();
        var unavailableFrames = 0;
        var pausedFrames = 0;
        var recognizedRegions = 0;
        long peakWorkingSet = process.WorkingSet64;

        while (wall.Elapsed < TimeSpan.FromSeconds(durationSeconds) && !cancellationToken.IsCancellationRequested)
        {
            var cycle = Stopwatch.StartNew();
            try
            {
                window = locator.Find() ?? throw new InvalidOperationException("AOE2 DE window disappeared.");
                var captureTimer = Stopwatch.StartNew();
                var frame = await capture.CaptureAsync(window, cancellationToken);
                captureTimer.Stop();
                var ocrTimer = Stopwatch.StartNew();
                var result = analyzer.AnalyzeFrame(frame.BgraPixels, frame.Width, frame.Height, frame.CapturedAt);
                ocrTimer.Stop();
                captureLatencies.Add(captureTimer.Elapsed.TotalMilliseconds);
                latencies.Add(ocrTimer.Elapsed.TotalMilliseconds);
                recognizedRegions += analyzer.LastRecognizedRegionCount;
                if (result.Fields.Values.Any(item => item.Value is null)) unavailableFrames++;
                if (result.IsPauseMenuVisible) pausedFrames++;
                process.Refresh();
                peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures.Add(exception.Message);
            }

            var remaining = TimeSpan.FromMilliseconds(500) - cycle.Elapsed;
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining, cancellationToken);
        }

        wall.Stop();
        process.Refresh();
        var cpu = process.TotalProcessorTime - cpuStarted;
        var averageCpu = cpu.TotalMilliseconds / Math.Max(1, wall.Elapsed.TotalMilliseconds) / Environment.ProcessorCount * 100;
        return new LivePerformanceReport(
            1, DateTimeOffset.UtcNow, wall.Elapsed.TotalSeconds, latencies.Count, failures.Count,
            averageCpu, peakWorkingSet / 1024d / 1024d,
            Average(captureLatencies), Percentile(captureLatencies, .95),
            Average(latencies), Percentile(latencies, .95),
            latencies.Count == 0 ? 0 : (double)recognizedRegions / latencies.Count,
            unavailableFrames, pausedFrames, failures);
    }

    public static void WriteJson(LivePerformanceReport report, string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static double Average(IReadOnlyCollection<double> values) => values.Count == 0 ? 0 : values.Average();
    private static double Percentile(IEnumerable<double> values, double percentile)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0 ? 0 : sorted[Math.Clamp((int)Math.Ceiling(percentile * sorted.Length) - 1, 0, sorted.Length - 1)];
    }
}

public sealed record LivePerformanceReport(
    int SchemaVersion,
    DateTimeOffset GeneratedAt,
    double DurationSeconds,
    int Frames,
    int Failures,
    double AverageCpuPercent,
    double PeakWorkingSetMegabytes,
    double AverageCaptureLatencyMilliseconds,
    double P95CaptureLatencyMilliseconds,
    double AverageOcrLatencyMilliseconds,
    double P95OcrLatencyMilliseconds,
    double AverageRecognizedRegionsPerFrame,
    int UnavailableFrames,
    int PausedFrames,
    IReadOnlyList<string> FailureDetails);
