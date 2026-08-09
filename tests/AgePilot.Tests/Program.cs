using AgePilot.Core.Observations;
using AgePilot.Core;
using AgePilot.Core.History;
using AgePilot.Core.Rules;
using AgePilot.Vision.Geometry;
using AgePilot.Vision.Images;
using AgePilot.Vision.Ocr;
using AgePilot.Vision.Profiles;
using AgePilot.Vision.Observations;
using AgePilot.Core.Configuration;
using AgePilot.Infrastructure.Persistence;
using AgePilot.Core.Recommendations;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using AgePilot.Vision.Benchmarking;
using AgePilot.Infrastructure.Diagnostics;

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
    ("Game age OCR text is parsed", GameAgeTextIsParsed),
    ("Pause menu OCR text is parsed", PauseMenuTextIsParsed),
    ("Reference HUD OCR matches ground truth", ReferenceHudOcrMatchesGroundTruth),
    ("Live HUD zero OCR matches ground truth", LiveHudZeroOcrMatchesGroundTruth),
    ("Screenshot manifest OCR matches every ground truth", ScreenshotManifestOcrMatchesEveryGroundTruth),
    ("Vision benchmark metrics meet current manifest", VisionBenchmarkMetricsMeetCurrentManifest),
    ("Paused screenshot is detected", PausedScreenshotIsDetected),
    ("Adaptive OCR reuses unchanged HUD regions", AdaptiveOcrReusesUnchangedHudRegions),
    ("Game lifecycle transitions are explicit", GameLifecycleTransitionsAreExplicit),
    ("OCR result becomes confirmed game state", OcrResultBecomesConfirmedGameState),
    ("Population critical has priority", PopulationCriticalHasPriority),
    ("Castle ready rule uses age and resources", CastleReadyUsesAgeAndResources),
    ("Resource overflow selects highest resource", ResourceOverflowSelectsHighestResource),
    ("Active recommendation remains visible", ActiveRecommendationRemainsVisible),
    ("Dismissed recommendation returns after resolution", DismissedRecommendationReturnsAfterResolution),
    ("Low confidence zero requires temporal confirmation", LowConfidenceZeroRequiresTemporalConfirmation),
    ("Unavailable observation is not usable", UnavailableObservationIsNotUsable),
    ("Settings round trip", SettingsRoundTrip),
    ("Local diagnostic log is JSON lines", LocalDiagnosticLogIsJsonLines),
    ("SQLite session round trip", SqliteSessionRoundTrip),
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

static void GameAgeTextIsParsed()
{
    Equal(GameAge.Dark, GameAgeTextParser.Parse("黑暗時代"));
    Equal(GameAge.Feudal, GameAgeTextParser.Parse("封建時代"));
    Equal(GameAge.Castle, GameAgeTextParser.Parse("城堡時代"));
    Equal(GameAge.Imperial, GameAgeTextParser.Parse("帝王時代"));
    Equal<GameAge?>(null, GameAgeTextParser.Parse("主選單"));
}

static void PauseMenuTextIsParsed()
{
    Equal(true, PauseMenuTextParser.IsVisible("主選單"));
    Equal(true, PauseMenuTextParser.IsVisible(" 主 菜 单 "));
    Equal(false, PauseMenuTextParser.IsVisible("黑暗時代"));
    Equal(false, PauseMenuTextParser.IsVisible(null));
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
    Equal(GameAge.Dark, result.Age);
}

static void LiveHudZeroOcrMatchesGroundTruth()
{
    var imagePath = FindRepositoryFile("doc", "Snipaste_2026-08-09_18-03-12.jpg");
    var profile = HudProfileLoader.Load(
        FindRepositoryFile("config", "hud", "aoe2de-zh-tw-2560x1440-50.json"));
    using var engine = new PaddleNumericOcrEngine();
    var result = new HudOcrAnalyzer(engine).AnalyzeJpeg(imagePath, profile);

    Equal(0, result.Fields[HudField.Food].Value);
    Equal(true, result.Fields[HudField.Food].Confidence >= 0.45);
    Equal(new PopulationValue(4, 5), result.Population);
    Equal(GameAge.Dark, result.Age);
}

static void ScreenshotManifestOcrMatchesEveryGroundTruth()
{
    var manifestPath = FindRepositoryFile("testdata", "screenshots", "manifest.json");
    var manifestDirectory = Path.GetDirectoryName(manifestPath)!;
    using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
    using var engine = new PaddleNumericOcrEngine();

    foreach (var sample in document.RootElement.GetProperty("samples").EnumerateArray())
    {
        var id = sample.GetProperty("id").GetString()!;
        var imagePath = Path.GetFullPath(sample.GetProperty("image").GetString()!, manifestDirectory);
        var profilePath = Path.GetFullPath(sample.GetProperty("profile").GetString()!, manifestDirectory);
        var truth = sample.GetProperty("groundTruth");
        var result = new HudOcrAnalyzer(engine).AnalyzeJpeg(imagePath, HudProfileLoader.Load(profilePath));

        EqualContext(truth.GetProperty("wood").GetInt32(), result.Fields[HudField.Wood].Value, id);
        EqualContext(truth.GetProperty("food").GetInt32(), result.Fields[HudField.Food].Value, id);
        EqualContext(truth.GetProperty("gold").GetInt32(), result.Fields[HudField.Gold].Value, id);
        EqualContext(truth.GetProperty("stone").GetInt32(), result.Fields[HudField.Stone].Value, id);
        EqualContext(truth.GetProperty("population").GetInt32(), result.Population?.Current, id);
        EqualContext(truth.GetProperty("populationCap").GetInt32(), result.Population?.Cap, id);
        if (truth.TryGetProperty("age", out var age)) EqualContext(Enum.Parse<GameAge>(age.GetString()!), result.Age, id);
    }
}

static void VisionBenchmarkMetricsMeetCurrentManifest()
{
    var report = VisionBenchmarkRunner.Run(FindRepositoryFile("testdata", "screenshots", "manifest.json"));
    Equal(1d, report.FieldExactAccuracy);
    Equal(1d, report.FrameExactAccuracy);
    Equal(0d, report.HighConfidenceErrorRate);
    Equal(0d, report.FalseRecommendationRate);
    Equal(1d, report.RecommendationExactRate);
}

static void PausedScreenshotIsDetected()
{
    var profile = HudProfileLoader.Load(FindRepositoryFile("config", "hud", "aoe2de-zh-tw-2560x1440-50.json"));
    using var engine = new PaddleNumericOcrEngine();
    var paused = new HudOcrAnalyzer(engine).AnalyzeJpeg(
        FindRepositoryFile("doc", "Snipaste_2026-08-09_18-54-40.jpg"), profile);
    var active = new HudOcrAnalyzer(engine).AnalyzeJpeg(
        FindRepositoryFile("doc", "Snipaste_2026-08-09_18-54-29.jpg"), profile);

    Equal(true, paused.IsPauseMenuVisible);
    Equal(false, active.IsPauseMenuVisible);
}

static void AdaptiveOcrReusesUnchangedHudRegions()
{
    var profile = HudProfileLoader.Load(FindRepositoryFile("config", "hud", "aoe2de-zh-tw-2560x1440-50.json"));
    var image = BgraImageLoader.Load(FindRepositoryFile("doc", "Snipaste_2026-08-09_18-54-29.jpg"));
    using var engine = new PaddleNumericOcrEngine();
    var analyzer = new AdaptiveHudOcrAnalyzer(engine, profile);
    var now = DateTimeOffset.UtcNow;

    var first = analyzer.AnalyzeFrame(image.Pixels, image.Width, image.Height, now);
    Equal(7, analyzer.LastRecognizedRegionCount);
    var second = analyzer.AnalyzeFrame(image.Pixels, image.Width, image.Height, now.AddMilliseconds(500));

    Equal(0, analyzer.LastRecognizedRegionCount);
    Equal(first.Population, second.Population);
    Equal(first.Fields[HudField.Food], second.Fields[HudField.Food]);
}

static void GameLifecycleTransitionsAreExplicit()
{
    var tracker = new GameLifecycleTracker();
    Equal(GameLifecycleState.GameNotFound, tracker.ObserveWindow(false));
    Equal(GameLifecycleState.GameDetected, tracker.ObserveWindow(true));
    Equal(GameLifecycleState.GameLoading, tracker.ObserveFrame(false, 0));
    Equal(GameLifecycleState.GameActive, tracker.ObserveFrame(false, 6));
    Equal(GameLifecycleState.GamePaused, tracker.ObserveFrame(true, 6));
    Equal(GameLifecycleState.GameUnavailable, tracker.ObserveFailure());
    Equal(GameLifecycleState.GameEnded, tracker.ObserveWindow(false));
    Equal(GameLifecycleState.GameNotFound, tracker.ObserveWindow(false));
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

static void CastleReadyUsesAgeAndResources()
{
    var now = DateTimeOffset.UtcNow;
    var state = new GameState { Age = GameAge.Feudal, Food = Confirmed(800, now), Gold = Confirmed(200, now) };
    var result = new CastleReadyRule().Evaluate(state, new GameHistory());
    Equal("R006", result?.Id);
}

static void ResourceOverflowSelectsHighestResource()
{
    var now = DateTimeOffset.UtcNow;
    var state = new GameState { Food = Confirmed(1600, now), Wood = Confirmed(1900, now), Gold = Confirmed(500, now) };
    var result = new ResourceOverflowRule().Evaluate(state, new GameHistory());
    Equal(true, result?.Title.Contains("木材", StringComparison.Ordinal));
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

static void DismissedRecommendationReturnsAfterResolution()
{
    var recommendation = new Recommendation("R001", CoachSeverity.Warning, "準備房屋", "test", 90, 0.9, TimeSpan.Zero);
    var coordinator = new RecommendationCoordinator();
    Equal(1, coordinator.Apply([recommendation]).Count);
    coordinator.Dismiss("R001");
    Equal(0, coordinator.Apply([recommendation]).Count);
    Equal(0, coordinator.Apply([]).Count);
    Equal(1, coordinator.Apply([recommendation]).Count);
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

static void SettingsRoundTrip()
{
    var path = Path.Combine(Path.GetTempPath(), $"agepilot-settings-{Guid.NewGuid():N}.json");
    try
    {
        var store = new JsonSettingsStore(path);
        store.Save(new AppSettings { HudProfilePath = "profile.json", OverlayOpacity = 0.85, ScanIntervalMilliseconds = 1000, EnableSessionRecording = false, EnableLocalDiagnostics = false });
        var loaded = store.Load();
        Equal("profile.json", loaded.HudProfilePath);
        Equal(0.85, loaded.OverlayOpacity);
        Equal(1000, loaded.ScanIntervalMilliseconds);
        Equal(false, loaded.EnableSessionRecording);
        Equal(false, loaded.EnableLocalDiagnostics);
    }
    finally { if (File.Exists(path)) File.Delete(path); }
}

static void LocalDiagnosticLogIsJsonLines()
{
    var directory = Path.Combine(Path.GetTempPath(), $"agepilot-log-{Guid.NewGuid():N}");
    var path = Path.Combine(directory, "agepilot.jsonl");
    try
    {
        var logger = new LocalJsonLineLogger(path);
        logger.Write("game.lifecycle", new { state = "GameActive" });
        using var json = JsonDocument.Parse(File.ReadAllLines(path).Single());
        Equal("game.lifecycle", json.RootElement.GetProperty("eventName").GetString());
        Equal("GameActive", json.RootElement.GetProperty("data").GetProperty("state").GetString());
    }
    finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}

static void SqliteSessionRoundTrip()
{
    var directory = Path.Combine(Path.GetTempPath(), $"agepilot-db-{Guid.NewGuid():N}");
    var databasePath = Path.Combine(directory, "agepilot.db");
    try
    {
        var repository = new SqliteSessionRepository(databasePath);
        repository.InitializeAsync().GetAwaiter().GetResult();
        var startedAt = DateTimeOffset.UtcNow;
        var sessionId = repository.StartSessionAsync("farmer", startedAt).GetAwaiter().GetResult();
        var state = new GameState
        {
            Food = Confirmed(300, startedAt),
            Wood = Confirmed(700, startedAt),
            Population = Confirmed(19, startedAt),
            PopulationCap = Confirmed(20, startedAt),
        };
        repository.AddSnapshotAsync(sessionId, startedAt, state).GetAwaiter().GetResult();
        repository.AddRecommendationAsync(sessionId, startedAt,
            new Recommendation("R001", CoachSeverity.Warning, "準備房屋", "人口空間不足。", 90, 0.95, TimeSpan.FromSeconds(45)))
            .GetAwaiter().GetResult();
        repository.EndSessionAsync(sessionId, startedAt.AddMinutes(1)).GetAwaiter().GetResult();

        var summary = repository.GetRecentSessionsAsync().GetAwaiter().GetResult().Single();
        Equal(sessionId, summary.Id);
        Equal("farmer", summary.Profile);
        Equal(1, summary.SnapshotCount);
        Equal(1, summary.RecommendationCount);
        Equal(300, summary.PeakFood);
        Equal(700, summary.PeakWood);
        Equal(19, summary.PeakPopulation);
        Equal(true, summary.EndedAt is not null);
    }
    finally
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
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

static void EqualContext<T>(T expected, T actual, string context)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{context}: expected '{expected}', got '{actual}'.");
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
