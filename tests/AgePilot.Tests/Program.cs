using AgePilot.Core.Observations;
using AgePilot.Core;
using AgePilot.Core.History;
using AgePilot.Core.Rules;
using AgePilot.Vision.Geometry;
using AgePilot.Vision.Images;
using AgePilot.Vision.Ocr;
using AgePilot.Vision.Profiles;
using AgePilot.Vision.Observations;
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
    ("Population OCR text is parsed", PopulationTextIsParsed),
    ("Reference HUD OCR matches ground truth", ReferenceHudOcrMatchesGroundTruth),
    ("Live HUD zero OCR matches ground truth", LiveHudZeroOcrMatchesGroundTruth),
    ("OCR result becomes confirmed game state", OcrResultBecomesConfirmedGameState),
    ("Population critical has priority", PopulationCriticalHasPriority),
    ("Active recommendation remains visible", ActiveRecommendationRemainsVisible),
    ("Low confidence zero requires temporal confirmation", LowConfidenceZeroRequiresTemporalConfirmation),
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

static void PopulationTextIsParsed()
{
    Equal(new PopulationValue(4, 5), PopulationTextParser.Parse(" 4/5 "));
    Equal(new PopulationValue(83, 100), PopulationTextParser.Parse("83 / 100"));
    Equal<PopulationValue?>(null, PopulationTextParser.Parse("100/80"));
}

static void ReferenceHudOcrMatchesGroundTruth()
{
    var imagePath = FindRepositoryFile("doc", "Snipaste_2026-08-09_16-29-15.jpg");
    var profile = HudProfileLoader.Load(
        FindRepositoryFile("config", "hud", "aoe2de-zh-tw-2560x1440-50.json"));
    using var engine = new PaddleNumericOcrEngine();
    var result = new HudOcrAnalyzer(engine).AnalyzeJpeg(imagePath, profile);

    Equal(200, result.Fields[HudField.Wood].Value);
    Equal(200, result.Fields[HudField.Food].Value);
    Equal(100, result.Fields[HudField.Gold].Value);
    Equal(200, result.Fields[HudField.Stone].Value);
    Equal(new PopulationValue(4, 5), result.Population);
}

static void LiveHudZeroOcrMatchesGroundTruth()
{
    var imagePath = FindRepositoryFile("doc", "Snipaste_2026-08-09_18-03-12.jpg");
    var profile = HudProfileLoader.Load(
        FindRepositoryFile("config", "hud", "aoe2de-zh-tw-2560x1440-50.json"));
    using var engine = new PaddleNumericOcrEngine();
    var result = new HudOcrAnalyzer(engine).AnalyzeJpeg(imagePath, profile);

    Equal(0, result.Fields[HudField.Food].Value);
    Equal(true, result.Fields[HudField.Food].Confidence is >= 0.45 and < 0.7);
    Equal(new PopulationValue(4, 5), result.Population);
}

static void OcrResultBecomesConfirmedGameState()
{
    var fields = new Dictionary<HudField, OcrResult>
    {
        [HudField.Wood] = new("200", 200, 0.99),
        [HudField.Food] = new("200", 200, 0.99),
        [HudField.Gold] = new("100", 100, 0.99),
        [HudField.Stone] = new("200", 200, 0.99),
        [HudField.Population] = new("4/5", 45, 0.81),
    };
    var raw = new HudOcrResult(fields, new PopulationValue(4, 5));
    var state = new TemporalGameStateEstimator().Update(raw, DateTimeOffset.UtcNow);

    Equal(200, state.Wood?.Value);
    Equal(4, state.Population?.Value);
    Equal(5, state.PopulationCap?.Value);
    Equal(true, state.Population?.IsUsable);
}

static void PopulationCriticalHasPriority()
{
    var now = DateTimeOffset.UtcNow;
    var state = new GameState
    {
        Population = Confirmed(5, now),
        PopulationCap = Confirmed(5, now),
        Wood = Confirmed(900, now),
        Food = Confirmed(200, now),
    };
    var engine = new CoachEngine(new ICoachRule[]
    {
        new WoodOverflowRule(),
        new PopulationLowRule(),
        new PopulationCriticalRule(),
    });

    var recommendations = engine.Evaluate(state, new GameHistory());
    Equal("R002", recommendations[0].Id);
    Equal(2, recommendations.Count);
}

static void ActiveRecommendationRemainsVisible()
{
    var now = DateTimeOffset.UtcNow;
    var state = new GameState
    {
        Population = Confirmed(4, now),
        PopulationCap = Confirmed(5, now),
    };
    var engine = new CoachEngine([new PopulationLowRule()]);
    var history = new GameHistory();

    Equal("R001", engine.Evaluate(state, history).Single().Id);
    Equal("R001", engine.Evaluate(state, history).Single().Id);
}

static void LowConfidenceZeroRequiresTemporalConfirmation()
{
    var fields = new Dictionary<HudField, OcrResult>
    {
        [HudField.Wood] = new("200", 200, 0.99),
        [HudField.Food] = new("0", 0, 0.505),
        [HudField.Gold] = new("100", 100, 0.99),
        [HudField.Stone] = new("200", 200, 0.99),
        [HudField.Population] = new("4/5", 45, 0.80),
    };
    var raw = new HudOcrResult(fields, new PopulationValue(4, 5));
    var estimator = new TemporalGameStateEstimator();

    var first = estimator.Update(raw, DateTimeOffset.UtcNow);
    var second = estimator.Update(raw, DateTimeOffset.UtcNow.AddMilliseconds(500));

    Equal(false, first.Food?.IsUsable);
    Equal(0, second.Food?.Value);
    Equal(true, second.Food?.IsUsable);
    Equal(true, second.Food?.Confidence < 0.7);
}

static ObservedValue<int> Confirmed(int value, DateTimeOffset at) =>
    new(value, 0.95, at, ObservationStatus.Confirmed);

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
