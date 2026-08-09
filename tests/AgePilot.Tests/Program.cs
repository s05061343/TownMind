using AgePilot.Core.Observations;
using AgePilot.Vision.Geometry;
using AgePilot.Vision.Images;
using AgePilot.Vision.Ocr;
using AgePilot.Vision.Profiles;
using System.Text.Json;

var tests = new (string Name, Action Run)[]
{
    ("Normalized ROI maps calibration pixels", NormalizedRoiMapsCalibrationPixels),
    ("Normalized ROI scales dynamically", NormalizedRoiScalesDynamically),
    ("Invalid ROI is rejected", InvalidRoiIsRejected),
    ("HUD profile is complete", HudProfileIsComplete),
    ("JPEG dimensions are read", JpegDimensionsAreRead),
    ("Screenshot manifest references existing assets", ScreenshotManifestReferencesExistingAssets),
    ("Numeric OCR text is normalized", NumericTextIsNormalized),
    ("Unavailable observation is not usable", UnavailableObservationIsNotUsable),
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
return failures == 0 ? 0 : 1;

static void NormalizedRoiMapsCalibrationPixels()
{
    var region = new NormalizedRect(66d / 2560, 15d / 1440, 52d / 2560, 36d / 1440);
    Equal(new PixelRect(66, 15, 52, 36), region.ToPixels(2560, 1440));
}

static void NormalizedRoiScalesDynamically()
{
    var region = new NormalizedRect(0.25, 0.25, 0.5, 0.5);
    Equal(new PixelRect(480, 270, 960, 540), region.ToPixels(1920, 1080));
}

static void InvalidRoiIsRejected()
{
    Throws<InvalidDataException>(() => new NormalizedRect(0.9, 0.1, 0.2, 0.2).Validate());
}

static void HudProfileIsComplete()
{
    var profile = HudProfileLoader.Load(FindRepositoryFile("config", "hud", "aoe2de-zh-tw-2560x1440-50.json"));
    Equal(5, profile.Regions.Count);
    Equal(50, profile.HudScalePercent);
}

static void JpegDimensionsAreRead()
{
    var size = BmpInfoReader.ReadJpegSize(
        FindRepositoryFile("doc", "Snipaste_2026-08-09_16-29-15.jpg"));
    Equal(new ImageSize(2560, 1440), size);
}

static void NumericTextIsNormalized()
{
    Equal(1200, NumericTextParser.ParseNonNegativeInteger(" 1,200 "));
    Equal(45, NumericTextParser.ParseNonNegativeInteger("O45"));
    Equal<int?>(null, NumericTextParser.ParseNonNegativeInteger("---"));
}

static void ScreenshotManifestReferencesExistingAssets()
{
    var manifestPath = FindRepositoryFile("testdata", "screenshots", "manifest.json");
    using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
    var sample = manifest.RootElement.GetProperty("samples")[0];
    var manifestDirectory = Path.GetDirectoryName(manifestPath)
        ?? throw new InvalidOperationException("Manifest directory is unavailable.");

    var imagePath = Path.GetFullPath(sample.GetProperty("image").GetString()!, manifestDirectory);
    var profilePath = Path.GetFullPath(sample.GetProperty("profile").GetString()!, manifestDirectory);

    Equal(true, File.Exists(imagePath));
    Equal(true, File.Exists(profilePath));
    Equal(200, sample.GetProperty("groundTruth").GetProperty("wood").GetInt32());
    Equal(5, sample.GetProperty("groundTruth").GetProperty("populationCap").GetInt32());
}

static void UnavailableObservationIsNotUsable()
{
    var observation = ObservedValue<int>.Unavailable(DateTimeOffset.UtcNow);
    Equal(false, observation.IsUsable);
    Equal<int?>(null, observation.Value);
}

static string FindRepositoryFile(params string[] parts)
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        var candidateParts = new[] { directory.FullName }.Concat(parts).ToArray();
        var candidate = Path.Combine(candidateParts);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        directory = directory.Parent;
    }

    throw new FileNotFoundException($"Repository file not found: {Path.Combine(parts)}");
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}

static void Throws<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected exception {typeof(TException).Name}.");
}
