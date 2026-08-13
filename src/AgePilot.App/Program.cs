using AgePilot.Vision.Capture;
using AgePilot.Vision.Images;
using AgePilot.Vision.Profiles;
using AgePilot.Vision.Ocr;
using AgePilot.App;
using System.Windows;
using System.IO;
using AgePilot.Core.Configuration;
using AgePilot.Infrastructure.Persistence;
using AgePilot.Vision.Benchmarking;
using AgePilot.Infrastructure.Diagnostics;
using System.Runtime.InteropServices;
using AgePilot.Core;
using AgePilot.Core.History;
using AgePilot.Core.Observations;
using AgePilot.Core.Planning;
using AgePilot.Core.Automation;
using AgePilot.Infrastructure.Planning;
using System.Text.Json;

var isGui = args.Length == 0 || args is ["overlay", _];
Mutex? guiMutex = null;
if (isGui)
{
    guiMutex = new Mutex(true, "Local\\AgePilot.SingleGuiInstance", out var isFirstInstance);
    if (!isFirstInstance)
    {
        System.Windows.MessageBox.Show("AgePilot 已經在執行。請使用現有的系統匣圖示或先結束舊程序。", "AgePilot",
            MessageBoxButton.OK, MessageBoxImage.Information);
        guiMutex.Dispose();
        return 3;
    }
}

if (args.Length > 0)
{
    ConsoleHost.AttachToParent();
}

try
{
return args switch
{
    [] => RunDashboard(),
    ["inspect", var imagePath, var profilePath] => Inspect(imagePath, profilePath),
    ["find-game"] => FindGame(),
    ["capture", var outputPath] => await CaptureAsync(outputPath),
    ["ocr-image", var imagePath, var profilePath] => OcrImage(imagePath, profilePath),
    ["scan-live", var profilePath] => await ScanLiveAsync(profilePath),
    ["overlay", var profilePath] => RunOverlay(profilePath),
    ["vision-report", var manifestPath, var outputPath] => VisionReport(manifestPath, outputPath),
    ["replay-report", var manifestPath, var outputPath, var cycles] => ReplayReport(manifestPath, outputPath, cycles),
    ["live-benchmark", var profilePath, var outputPath, var seconds] => await LiveBenchmarkAsync(profilePath, outputPath, seconds),
    ["plan-smoke"] => await PlanSmokeAsync(),
    ["vlm-image", var imagePath] => await VlmImageAsync(imagePath),
    _ => ShowUsage(),
};
}
finally
{
    guiMutex?.Dispose();
}

static int Inspect(string imagePath, string profilePath)
{
    var size = BmpInfoReader.ReadJpegSize(imagePath);
    var profile = HudProfileLoader.Load(profilePath);

    Console.WriteLine($"Image: {Path.GetFullPath(imagePath)}");
    Console.WriteLine($"Size: {size.Width}x{size.Height}");
    Console.WriteLine($"Profile: {profile.Id}");
    Console.WriteLine($"Calibration: {profile.CalibrationWidth}x{profile.CalibrationHeight}, HUD {profile.HudScalePercent}%");

    foreach (var (field, region) in profile.Regions.OrderBy(pair => pair.Key))
    {
        var pixels = region.ToPixels(size.Width, size.Height);
        Console.WriteLine($"{field,-12} x={pixels.X,4} y={pixels.Y,3} w={pixels.Width,3} h={pixels.Height,3}");
    }

    return 0;
}

static int FindGame()
{
    var window = new WindowsGameWindowLocator().Find();
    if (window is null)
    {
        Console.WriteLine("AOE2 DE window not found.");
        return 2;
    }

    Console.WriteLine($"Found AOE2 DE: PID={window.ProcessId}, HWND=0x{window.Handle:X}, Title={window.Title}");
    return 0;
}

static async Task<int> CaptureAsync(string outputPath)
{
    var window = new WindowsGameWindowLocator().Find();
    if (window is null)
    {
        Console.WriteLine("AOE2 DE window not found.");
        return 2;
    }

    var frame = await new WindowsGdiFrameCapture()
        .CaptureAsync(window, CancellationToken.None);
    BgraBitmapWriter.Write(outputPath, frame.Width, frame.Height, frame.BgraPixels.Span);

    Console.WriteLine($"Captured {frame.Width}x{frame.Height} to {Path.GetFullPath(outputPath)}");
    return 0;
}

static int OcrImage(string imagePath, string profilePath)
{
    var profile = HudProfileLoader.Load(ResolveInputPath(profilePath));
    var startedAt = DateTimeOffset.UtcNow;
    Console.WriteLine("Initializing local OCR model...");
    using var engine = new PaddleNumericOcrEngine();
    Console.WriteLine($"OCR model ready in {(DateTimeOffset.UtcNow - startedAt).TotalSeconds:F1}s. Analyzing HUD...");
    var result = new HudOcrAnalyzer(engine).AnalyzeJpeg(imagePath, profile);

    PrintOcrResult(result);
    return 0;
}

static async Task<int> ScanLiveAsync(string profilePath)
{
    var window = new WindowsGameWindowLocator().Find();
    if (window is null)
    {
        Console.WriteLine("AOE2 DE window not found. Start the game and enter a match first.");
        return 2;
    }

    var profile = HudProfileLoader.Load(ResolveInputPath(profilePath));
    Console.WriteLine($"Found AOE2 DE: PID={window.ProcessId}, Title={window.Title}");
    var frame = await new WindowsGdiFrameCapture().CaptureAsync(window, CancellationToken.None);
    Console.WriteLine($"Captured {frame.Width}x{frame.Height}. Initializing local OCR model...");

    using var engine = new PaddleNumericOcrEngine();
    var result = new HudOcrAnalyzer(engine)
        .AnalyzeFrame(frame.BgraPixels, frame.Width, frame.Height, profile);
    PrintOcrResult(result);
    return 0;
}

static void PrintOcrResult(HudOcrResult result)
{
    Console.WriteLine($"Age          {result.Age?.ToString() ?? "unavailable"}  raw='{result.AgeObservation?.RawText ?? string.Empty}' confidence={result.AgeObservation?.Confidence:P1}");
    foreach (var field in Enum.GetValues<HudField>())
    {
        var observation = result.Fields[field];
        if (field == HudField.Population && result.Population is { } population)
        {
            Console.WriteLine($"{field,-12} {population.Current}/{population.Cap}  raw='{observation.RawText}' confidence={observation.Confidence:P1}");
        }
        else
        {
            Console.WriteLine($"{field,-12} {observation.Value?.ToString() ?? "unavailable"}  raw='{observation.RawText}' confidence={observation.Confidence:P1}");
        }
    }

}

static int RunOverlay(string profilePath)
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        var logger = LocalJsonLineLogger.CreateDefault();
        try
        {
            var application = new System.Windows.Application();
            using var exceptions = GlobalExceptionMiddleware.Install(application, logger, "overlay");
            var settingsStore = JsonSettingsStore.CreateDefault();
            var settings = settingsStore.Load();
            settings.HudProfilePath = profilePath;
            application.Run(new OverlayWindow(ResolveInputPath(profilePath), settings, new MouseCapabilitySession(),
                sessionRepository: SqliteSessionRepository.CreateDefault(), logger: logger));
        }
        catch (Exception exception)
        {
            logger.WriteException("exception.gui_entry", exception, new { mode = "overlay" });
            failure = exception;
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    if (failure is not null)
    {
        Console.Error.WriteLine(failure);
        return 3;
    }

    return 0;
}

static int VisionReport(string manifestPath, string outputPath)
{
    var report = VisionBenchmarkRunner.Run(ResolveInputPath(manifestPath));
    VisionBenchmarkRunner.WriteJson(report, outputPath);
    Console.WriteLine($"Samples: {report.Samples.Count}");
    Console.WriteLine($"FieldExactAccuracy: {report.FieldExactAccuracy:P2}");
    Console.WriteLine($"FrameExactAccuracy: {report.FrameExactAccuracy:P2}");
    Console.WriteLine($"HighConfidenceErrorRate: {report.HighConfidenceErrorRate:P3}");
    Console.WriteLine($"UnavailableRate: {report.UnavailableRate:P2}");
    Console.WriteLine($"FalseRecommendationRate: {report.FalseRecommendationRate:P2}");
    Console.WriteLine($"RecommendationExactRate: {report.RecommendationExactRate:P2}");
    Console.WriteLine($"OCR latency average/p95: {report.AverageLatencyMilliseconds:F1}/{report.P95LatencyMilliseconds:F1} ms");
    Console.WriteLine($"Report: {Path.GetFullPath(outputPath)}");
    return report.Samples.All(sample => sample.FrameExact) ? 0 : 4;
}

static int ReplayReport(string manifestPath, string outputPath, string cyclesText)
{
    if (!int.TryParse(cyclesText, out var cycles))
    {
        Console.Error.WriteLine("cycles must be an integer between 1 and 1000.");
        return 1;
    }
    var report = ReplayBenchmark.Run(ResolveInputPath(manifestPath), cycles);
    ReplayBenchmark.WriteJson(report, outputPath);
    Console.WriteLine($"Frames/failures: {report.Frames}/{report.Failures}");
    Console.WriteLine($"CPU average: {report.AverageCpuPercent:F2}%");
    Console.WriteLine($"Peak working set: {report.PeakWorkingSetMegabytes:F1} MB");
    Console.WriteLine($"OCR latency average/p95: {report.AverageOcrLatencyMilliseconds:F1}/{report.P95OcrLatencyMilliseconds:F1} ms");
    Console.WriteLine($"Paused frames suppressed: {report.SuppressedPausedFrames}");
    Console.WriteLine($"OCR regions per frame: {report.AverageRecognizedRegionsPerFrame:F2}");
    Console.WriteLine($"Report: {Path.GetFullPath(outputPath)}");
    return report.Failures == 0 ? 0 : 5;
}

static async Task<int> LiveBenchmarkAsync(string profilePath, string outputPath, string secondsText)
{
    if (!int.TryParse(secondsText, out var seconds))
    {
        Console.Error.WriteLine("seconds must be an integer between 10 and 3600.");
        return 1;
    }
    try
    {
        var report = await LivePerformanceBenchmark.RunAsync(ResolveInputPath(profilePath), seconds, CancellationToken.None);
        LivePerformanceBenchmark.WriteJson(report, outputPath);
        Console.WriteLine($"Frames/failures: {report.Frames}/{report.Failures}");
        Console.WriteLine($"CPU average: {report.AverageCpuPercent:F2}%");
        Console.WriteLine($"Peak working set: {report.PeakWorkingSetMegabytes:F1} MB");
        Console.WriteLine($"Capture average/p95: {report.AverageCaptureLatencyMilliseconds:F1}/{report.P95CaptureLatencyMilliseconds:F1} ms");
        Console.WriteLine($"OCR average/p95: {report.AverageOcrLatencyMilliseconds:F1}/{report.P95OcrLatencyMilliseconds:F1} ms");
        Console.WriteLine($"OCR regions per frame: {report.AverageRecognizedRegionsPerFrame:F2}");
        Console.WriteLine($"Report: {Path.GetFullPath(outputPath)}");
        return report.Failures == 0 ? 0 : 6;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(exception.Message);
        return 2;
    }
}

static int RunDashboard()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        var logger = LocalJsonLineLogger.CreateDefault();
        try
        {
            var application = new System.Windows.Application();
            using var exceptions = GlobalExceptionMiddleware.Install(application, logger, "dashboard");
            var dashboard = new DashboardWindow(JsonSettingsStore.CreateDefault());
            application.SessionEnding += (_, _) => dashboard.PrepareForSystemExit();
            application.Run(dashboard);
        }
        catch (Exception exception)
        {
            logger.WriteException("exception.gui_entry", exception, new { mode = "dashboard" });
            failure = exception;
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is null) return 0;
    Console.Error.WriteLine(failure);
    return 3;
}

static int ShowUsage()
{
    Console.WriteLine("AgePilot Vision Spike");
    Console.WriteLine("  inspect <jpeg-path> <hud-profile-path>");
    Console.WriteLine("  find-game");
    Console.WriteLine("  capture <output-bmp-path>");
    Console.WriteLine("  ocr-image <jpeg-path> <hud-profile-path>");
    Console.WriteLine("  scan-live <hud-profile-path>");
    Console.WriteLine("  overlay <hud-profile-path>");
    Console.WriteLine("  vision-report <manifest-path> <output-json-path>");
    Console.WriteLine("  replay-report <manifest-path> <output-json-path> <cycles>");
    Console.WriteLine("  live-benchmark <hud-profile-path> <output-json-path> <seconds>");
    Console.WriteLine("  plan-smoke");
    Console.WriteLine("  vlm-image <jpeg-path>");
    return 1;
}

static async Task<int> VlmImageAsync(string imagePath)
{
    var now = DateTimeOffset.UtcNow;
    var image = BgraImageLoader.Load(ResolveInputPath(imagePath));
    var images = VisualPromptImageEncoder.Encode(image.Pixels, image.Width, image.Height,
        new AgePilot.Vision.Geometry.NormalizedRect(0, 0.66, 0.47, 0.34),
        new AgePilot.Vision.Geometry.NormalizedRect(0.80, 0.67, 0.20, 0.33),
        new AgePilot.Vision.Geometry.NormalizedRect(0, 0, 0.45, 0.06));
    using var planner = new LlamaServerPlanner(new AppSettings());
    var context = new SituationContext(new GameState(),
        GameHistorySummarizer.Summarize(new GameHistory(), TimeSpan.FromSeconds(1), now), null, null,
        [new PlanningEvent("visual_smoke", "請判斷目前畫面並提出一個安全的下一步；紅色建築預覽代表非法落點", now)], now,
        new VisualObservation(image.Width, image.Height, images,
            "top_hud=resources; command_panel=bottom-left; minimap=bottom-right; world=center", null, null));
    var result = await planner.PlanAsync(context, CancellationToken.None);
    if (!result.Success) { Console.Error.WriteLine(result.Error); return 8; }
    Console.WriteLine(JsonSerializer.Serialize(result.Plan!.VisualDecision,
        new JsonSerializerOptions { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }));
    return result.Plan.VisualDecision is null ? 9 : 0;
}

static async Task<int> PlanSmokeAsync()
{
    var now = DateTimeOffset.UtcNow;
    var settings = new AppSettings();
    using var planner = new LlamaServerPlanner(settings);
    var state = new GameState
    {
        Age = GameAge.Dark,
        Food = new(410, 0.95, now, ObservationStatus.Confirmed),
        Wood = new(260, 0.95, now, ObservationStatus.Confirmed),
        Gold = new(40, 0.95, now, ObservationStatus.Confirmed),
        Stone = new(200, 0.95, now, ObservationStatus.Confirmed),
        Population = new(18, 0.95, now, ObservationStatus.Confirmed),
        PopulationCap = new(20, 0.95, now, ObservationStatus.Confirmed),
    };
    var history = new GameHistory(); history.Add(state, now);
    var map = new MapContext(MapArchetype.Island, 0.62, 0.12, 0.18, 0.48, 2, 0.35, 0.88,
        ObservationStatus.Confirmed, now, 3);
    var context = new SituationContext(state, GameHistorySummarizer.Summarize(history, TimeSpan.FromSeconds(120), now),
        map, null, [new PlanningEvent("smoke_test", "固定島嶼經濟案例", now)], now);
    var result = await planner.PlanAsync(context, CancellationToken.None);
    if (!result.Success) { Console.Error.WriteLine(result.Error); return 7; }
    Console.WriteLine(JsonSerializer.Serialize(result.Plan, new JsonSerializerOptions { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }));
    return 0;
}

static string ResolveInputPath(string path)
{
    if (Path.IsPathRooted(path) || File.Exists(path)) return path;
    return Path.GetFullPath(path, AppContext.BaseDirectory);
}

internal static class ConsoleHost
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    public static void AttachToParent()
    {
        if (!AttachConsole(AttachParentProcess)) return;
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        Console.SetIn(new StreamReader(Console.OpenStandardInput()));
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint processId);
}
