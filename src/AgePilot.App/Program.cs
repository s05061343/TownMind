using AgePilot.Vision.Capture;
using AgePilot.Vision.Images;
using AgePilot.Vision.Profiles;

return args switch
{
    ["inspect", var imagePath, var profilePath] => Inspect(imagePath, profilePath),
    ["find-game"] => FindGame(),
    ["capture", var outputPath] => await CaptureAsync(outputPath),
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

static int ShowUsage()
{
    Console.WriteLine("AgePilot Vision Spike");
    Console.WriteLine("  inspect <jpeg-path> <hud-profile-path>");
    Console.WriteLine("  find-game");
    Console.WriteLine("  capture <output-bmp-path>");
    return 1;
}
