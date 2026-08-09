using AgePilot.Vision.Capture;
using AgePilot.Vision.Images;
using AgePilot.Vision.Profiles;
using AgePilot.Vision.Ocr;
using AgePilot.App;
using System.Windows;
using System.IO;

return args switch
{
    ["inspect", var imagePath, var profilePath] => Inspect(imagePath, profilePath),
    ["find-game"] => FindGame(),
    ["capture", var outputPath] => await CaptureAsync(outputPath),
    ["ocr-image", var imagePath, var profilePath] => OcrImage(imagePath, profilePath),
    ["scan-live", var profilePath] => await ScanLiveAsync(profilePath),
    ["overlay", var profilePath] => RunOverlay(profilePath),
    _ => ShowUsage(),
};

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
    var profile = HudProfileLoader.Load(profilePath);
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

    var profile = HudProfileLoader.Load(profilePath);
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
        try
        {
            var application = new Application();
            application.Run(new OverlayWindow(profilePath));
        }
        catch (Exception exception)
        {
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

static int ShowUsage()
{
    Console.WriteLine("AgePilot Vision Spike");
    Console.WriteLine("  inspect <jpeg-path> <hud-profile-path>");
    Console.WriteLine("  find-game");
    Console.WriteLine("  capture <output-bmp-path>");
    Console.WriteLine("  ocr-image <jpeg-path> <hud-profile-path>");
    Console.WriteLine("  scan-live <hud-profile-path>");
    Console.WriteLine("  overlay <hud-profile-path>");
    return 1;
}
