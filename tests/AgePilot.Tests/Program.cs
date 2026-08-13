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
using AgePilot.Core.Automation;
using AgePilot.Vision.World;
using AgePilot.Core.Planning;
using AgePilot.Infrastructure.Planning;

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
    ("Local diagnostic exceptions include stack context", LocalDiagnosticExceptionIncludesContext),
    ("SQLite session round trip", SqliteSessionRoundTrip),
    ("Brand assets include Windows icon sizes", BrandAssetsIncludeWindowsIconSizes),
    ("Application hotkeys are parsed", AutomationInputsAreParsed),
    ("Automation settings reject conflicting hotkeys", AutomationSettingsRejectConflictingHotkeys),
    ("Game plan validator rejects unsafe conditions", GamePlanValidatorRejectsUnsafeConditions),
    ("Strategy engine replans only the affected hierarchy", StrategyEngineReplansAffectedHierarchy),
    ("Mouse probe verifies movement and restores cursor", MouseProbeVerifiesAndRestores),
    ("Mouse probe fails when cursor does not move", MouseProbeRejectsMissingMovement),
    ("Minimap requires temporal confirmation", MinimapRequiresTemporalConfirmation),
    ("GPU device parser rejects CPU fallback", GpuDeviceParserRejectsCpuFallback),
    ("Quantified plan becomes concrete recommendation", QuantifiedPlanBecomesConcreteRecommendation),
    ("LLM quantities are corrected by observed game state", LlmQuantitiesAreCorrectedByGameState),
    ("Visual prompt encoder creates panorama and UI crops", VisualPromptEncoderCreatesImages),
    ("Visual player decision rejects unsafe mouse targets", VisualDecisionRejectsUnsafeValues),
    ("Command grid maps to calibrated mouse coordinates", CommandGridMapsCoordinates),
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
        store.Save(new AppSettings
        {
            HudProfilePath = "profile.json",
            OverlayOpacity = 0.85,
            ScanIntervalMilliseconds = 1000,
            EnableSessionRecording = false,
            EnableLocalDiagnostics = false,
            AutomationStartHotKey = "Alt+F10",
            AutomationStopHotKey = "Alt+F12",
            TargetAge = GameAge.Imperial,
            LlamaRuntimePath = @"D:\runtime\llama.cpp",
            LlmModelPath = @"D:\models\qwen.gguf",
        });
        var loaded = store.Load();
        Equal("profile.json", loaded.HudProfilePath);
        Equal(0.85, loaded.OverlayOpacity);
        Equal(1000, loaded.ScanIntervalMilliseconds);
        Equal(false, loaded.EnableSessionRecording);
        Equal(false, loaded.EnableLocalDiagnostics);
        Equal("Alt+F10", loaded.AutomationStartHotKey);
        Equal("Alt+F12", loaded.AutomationStopHotKey);
        Equal(GameAge.Imperial, loaded.TargetAge);
        Equal(@"D:\runtime\llama.cpp", loaded.LlamaRuntimePath);
        Equal(@"D:\models\qwen.gguf", loaded.LlmModelPath);
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

static void LocalDiagnosticExceptionIncludesContext()
{
    var directory = Path.Combine(Path.GetTempPath(), $"agepilot-exception-log-{Guid.NewGuid():N}");
    var path = Path.Combine(directory, "agepilot.jsonl");
    try
    {
        var logger = new LocalJsonLineLogger(path);
        logger.WriteException("exception.test", new InvalidOperationException("diagnostic-test"), new { source = "test" });
        using var json = JsonDocument.Parse(File.ReadAllLines(path).Single());
        Equal("exception.test", json.RootElement.GetProperty("eventName").GetString());
        Equal(Environment.ProcessId, json.RootElement.GetProperty("processId").GetInt32());
        var data = json.RootElement.GetProperty("data");
        Equal("System.InvalidOperationException", data.GetProperty("exceptionType").GetString());
        Equal("diagnostic-test", data.GetProperty("Message").GetString());
        Equal("test", data.GetProperty("context").GetProperty("source").GetString());
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

static void BrandAssetsIncludeWindowsIconSizes()
{
    var master = FindRepositoryFile("assets", "branding", "agepilot-logo-master.png");
    Equal(true, new FileInfo(master).Length > 100_000);

    var icon = FindRepositoryFile("assets", "branding", "agepilot.ico");
    using var stream = File.OpenRead(icon);
    using var reader = new BinaryReader(stream);
    Equal((ushort)0, reader.ReadUInt16());
    Equal((ushort)1, reader.ReadUInt16());
    var count = reader.ReadUInt16();
    Equal((ushort)7, count);

    var sizes = new HashSet<int>();
    for (var index = 0; index < count; index++)
    {
        var width = reader.ReadByte();
        var height = reader.ReadByte();
        sizes.Add(width == 0 ? 256 : width);
        Equal(width, height);
        reader.ReadBytes(14);
    }

    Equal(true, new[] { 16, 24, 32, 48, 64, 128, 256 }.All(sizes.Contains));
}

static void AutomationInputsAreParsed()
{
    var hotKey = GlobalHotKeyParser.Parse("Ctrl+F10");
    Equal(2, hotKey.Keys.Count);
    Equal("F10", hotKey.Keys[1]);

    Throws<InvalidDataException>(() => GlobalHotKeyParser.Parse("H,Q"));
}

static void AutomationSettingsRejectConflictingHotkeys()
{
    var settings = new AppSettings
    {
        AutomationStartHotKey = "Ctrl+F10",
        AutomationStopHotKey = "ctrl+f10",
    };
    Throws<InvalidDataException>(settings.Validate);
}

static void GamePlanValidatorRejectsUnsafeConditions()
{
    var now = DateTimeOffset.UtcNow;
    var valid = new GamePlan("p1", now, now.AddSeconds(60), "economy", "補人口", "人口仍有空間", 0.8,
        [], [], [new PlannedAction(PlannedActionKind.QueueVillager, 80, "維持生產")]);
    Equal(true, GamePlanValidator.Validate(valid, now).Success);
    var unsafePlan = valid with
    {
        Actions = [new PlannedAction(PlannedActionKind.QueueVillager, 80, "test", Preconditions: [new PlanCondition("mouseX", "eq", "42")])],
    };
    Equal(false, GamePlanValidator.Validate(unsafePlan, now).Success);
}

static void StrategyEngineReplansAffectedHierarchy()
{
    var now = DateTimeOffset.UtcNow;
    var first = HierarchyPlan("p1", now, "major-1", "medium-1", "minor-1");
    var minorUpdate = HierarchyPlan("p2", now.AddSeconds(10), "major-illegal", "medium-illegal", "minor-2");
    var mediumUpdate = HierarchyPlan("p3", now.AddSeconds(20), "major-illegal-2", "medium-2", "minor-3");
    var planner = new SequencePlanner([new(first), new(minorUpdate), new(mediumUpdate)]);
    using var engine = new StrategyEngine(planner);
    var state = new GameState { Food = Confirmed(100, now), Population = Confirmed(4, now), PopulationCap = Confirmed(5, now) };
    var history = new GameHistory(); history.Add(state, now);
    _ = engine.UpdateAsync(state, history, null, now, CancellationToken.None).GetAwaiter().GetResult();
    Equal("p1", engine.UpdateAsync(state, history, null, now, CancellationToken.None).GetAwaiter().GetResult().Plan?.PlanId);
    _ = engine.UpdateAsync(state, history, null, now.AddSeconds(5), CancellationToken.None).GetAwaiter().GetResult();
    Equal(1, planner.Contexts.Count);

    engine.ReportExecutionEvent(new PlanningEvent("visual_action_confirmed", "完成小動作", now.AddSeconds(10), PlanUpdateScope.Minor));
    _ = engine.UpdateAsync(state, history, null, now.AddSeconds(10), CancellationToken.None).GetAwaiter().GetResult();
    var afterMinor = engine.UpdateAsync(state, history, null, now.AddSeconds(10), CancellationToken.None).GetAwaiter().GetResult().Plan!;
    Equal("major-1", afterMinor.MajorDecision?.NodeId);
    Equal("medium-1", afterMinor.MediumDecision?.NodeId);
    Equal("minor-2", afterMinor.MinorDecision?.NodeId);

    engine.ReportExecutionEvent(new PlanningEvent("food_source_invalid", "羊已耗盡", now.AddSeconds(20), PlanUpdateScope.Medium));
    _ = engine.UpdateAsync(state, history, null, now.AddSeconds(20), CancellationToken.None).GetAwaiter().GetResult();
    var afterMedium = engine.UpdateAsync(state, history, null, now.AddSeconds(20), CancellationToken.None).GetAwaiter().GetResult().Plan!;
    Equal("major-1", afterMedium.MajorDecision?.NodeId);
    Equal("medium-2", afterMedium.MediumDecision?.NodeId);
    Equal(3, planner.Contexts.Count);
    Equal(PlanUpdateScope.Minor, planner.Contexts[1].AllowedUpdateScope);
    Equal(PlanUpdateScope.Medium, planner.Contexts[2].AllowedUpdateScope);
}

static GamePlan HierarchyPlan(string id, DateTimeOffset now, string majorId, string mediumId, string minorId) =>
    new(id, now, now.AddSeconds(60), "穩定發展經濟並升時代", "執行小判斷", "測試", 0.9, [], [],
        [new PlannedAction(PlannedActionKind.Reobserve, 80, "測試")],
        MajorDecision: Node(majorId, DecisionLevel.Major), MediumDecision: Node(mediumId, DecisionLevel.Medium),
        MinorDecision: Node(minorId, DecisionLevel.Minor));

static DecisionNode Node(string id, DecisionLevel level) =>
    new(id, level, $"{level} 目標", "理由", "可信畫面證據", "完成條件", "失敗條件");

static void MouseProbeVerifiesAndRestores()
{
    var backend = new FakeMouseBackend(new MousePoint(10, 20), new MousePoint(40, 20));
    var capability = new MouseCapabilitySession();
    var result = capability.Run(backend);
    Equal(true, result.Success);
    Equal(new MousePoint(10, 20), backend.Cursor);
    Equal(true, capability.IsValidFor(new nint(42)));
    Equal(2, backend.MoveCount);
}

static void MouseProbeRejectsMissingMovement()
{
    var backend = new FakeMouseBackend(new MousePoint(10, 20), new MousePoint(40, 20)) { IgnoreMovement = true };
    var capability = new MouseCapabilitySession();
    Equal(false, capability.Run(backend).Success);
    Equal(false, capability.IsVerified);
}

static void MinimapRequiresTemporalConfirmation()
{
    const int width = 90, height = 90;
    var pixels = new byte[width * height * 4];
    for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 150; pixels[i + 1] = 50; pixels[i + 2] = 30; pixels[i + 3] = 255; }
    var analyzer = new MinimapAnalyzer();
    var roi = new NormalizedRect(0, 0, 1, 1);
    Equal(ObservationStatus.Raw, analyzer.Analyze(pixels, width, height, roi, DateTimeOffset.UtcNow).Status);
    _ = analyzer.Analyze(pixels, width, height, roi, DateTimeOffset.UtcNow);
    var confirmed = analyzer.Analyze(pixels, width, height, roi, DateTimeOffset.UtcNow);
    Equal(ObservationStatus.Confirmed, confirmed.Status);
    Equal(MapArchetype.Island, confirmed.Archetype);
}

static void GpuDeviceParserRejectsCpuFallback()
{
    Equal<string?>(null, LlamaServerPlanner.ParseGpuDevice("Available devices:\n", "hip"));
    Equal("ROCm0", LlamaServerPlanner.ParseGpuDevice("Available devices:\n  ROCm0: AMD Radeon RX 9070 XT", "hip"));
    Equal("Vulkan0", LlamaServerPlanner.ParseGpuDevice("Vulkan0: AMD Radeon RX 9070 XT", "vulkan"));
}

static void QuantifiedPlanBecomesConcreteRecommendation()
{
    var now = DateTimeOffset.UtcNow;
    var plan = new GamePlan("p", now, now.AddSeconds(60), "經濟", "避免卡人口", "人口空間不足", 0.9, [], [],
        [new PlannedAction(PlannedActionKind.BuildHouse, 90, "先補住房", Quantity: 2, TargetPopulationCap: 30,
            TargetFoodWorkers: 12, TargetWoodWorkers: 8, TargetGoldWorkers: 3, TargetStoneWorkers: 0,
            RecheckSeconds: 20, SuccessCondition: "人口上限達到 30")]);
    var recommendation = GamePlanRecommendationAdapter.Convert(plan).Single();
    Equal("建造 2 間房屋", recommendation.Title);
    Equal(true, recommendation.Description.Contains("木材 8 人", StringComparison.Ordinal));
    Equal(true, recommendation.Description.Contains("目標配置（非目前實測）", StringComparison.Ordinal));
    Equal(true, recommendation.Description.Contains("人口上限達到 30", StringComparison.Ordinal));
}

static void LlmQuantitiesAreCorrectedByGameState()
{
    var now = DateTimeOffset.UtcNow;
    var state = new GameState { Population = Confirmed(18, now), PopulationCap = Confirmed(20, now) };
    var raw = new PlannedAction(PlannedActionKind.BuildHouse, 1, "住房", Quantity: 1, TargetPopulationCap: 20,
        TargetFoodWorkers: 5, TargetWoodWorkers: 5, RecheckSeconds: 30);
    var corrected = LlamaServerPlanner.NormalizeQuantities(raw, state);
    Equal(25, corrected.TargetPopulationCap);
    Equal(17, corrected.TargetFoodWorkers + corrected.TargetWoodWorkers + corrected.TargetGoldWorkers + corrected.TargetStoneWorkers);
    Equal(true, corrected.SuccessCondition.Contains("25", StringComparison.Ordinal));

    var withoutAllocation = raw with { TargetFoodWorkers = 0, TargetWoodWorkers = 0 };
    var allocated = LlamaServerPlanner.NormalizeQuantities(withoutAllocation, state,
        new MapContext(MapArchetype.Island, 0.6, 0.1, 0.2, 0.5, 2, 0.3, 0.9, ObservationStatus.Confirmed, now, 3));
    Equal(17, allocated.TargetFoodWorkers + allocated.TargetWoodWorkers + allocated.TargetGoldWorkers + allocated.TargetStoneWorkers);
    Equal(true, allocated.TargetWoodWorkers > allocated.TargetGoldWorkers);
}

static void VisualPromptEncoderCreatesImages()
{
    var image = BgraImageLoader.Load(FindRepositoryFile("img", "Snipaste_2026-08-12_20-38-47.jpg"));
    var encoded = VisualPromptImageEncoder.Encode(image.Pixels, image.Width, image.Height,
        new NormalizedRect(0, 0.66, 0.47, 0.34), new NormalizedRect(0.80, 0.67, 0.20, 0.33),
        new NormalizedRect(0, 0, 0.45, 0.06));
    Equal(4, encoded.Count);
    Equal("panorama", encoded[0].Name);
    Equal(true, encoded.All(item => item.MimeType == "image/jpeg" && item.Data.Length > 1000));
}

static void VisualDecisionRejectsUnsafeValues()
{
    var now = DateTimeOffset.UtcNow;
    var decision = new VisualPlayerDecision("已選村民", "建造兵營", "需要前置建築",
        new VisualToolAction(VisualToolKind.LeftClick, VisualCoordinateSpace.Panorama, "畫面中的村民", 0.3, 0.4), "出現建築預覽", 1000, 0.9);
    var plan = new GamePlan("vlm", now, now.AddSeconds(30), "visual-player", decision.Goal, decision.Reason,
        decision.Confidence, [], [], [new PlannedAction(PlannedActionKind.Reobserve, 80, decision.Reason)],
        VisualDecision: decision);
    Equal(true, GamePlanValidator.Validate(plan, now).Success);
    Equal(false, GamePlanValidator.Validate(plan with { VisualDecision = decision with
    {
        Action = decision.Action with { X = 1.2 },
    } }, now).Success);
    Equal(false, GamePlanValidator.Validate(plan with { VisualDecision = decision with
    {
        Action = new VisualToolAction(VisualToolKind.RightClick, VisualCoordinateSpace.CommandGrid, "非法命令格位", Row: 1, Column: 1),
    } }, now).Success);
}

static void CommandGridMapsCoordinates()
{
    var minimap = new NormalizedRect(0.8, 0.67, 0.19, 0.32);
    var grid = new NormalizedRect(0.01, 0.85, 0.15, 0.12);
    var action = new VisualToolAction(VisualToolKind.LeftClick, VisualCoordinateSpace.CommandGrid,
        "生產村民按鈕", Row: 2, Column: 3);
    Equal(true, MouseCoordinateMapper.TryResolve(action, minimap, grid, 3, 5,
        out var x, out var y, out _));
    Equal(true, Math.Abs(x - 0.085) < 0.0001);
    Equal(true, Math.Abs(y - 0.91) < 0.0001);
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

sealed class SequencePlanner(IEnumerable<PlanningResult> results) : IStrategicPlanner
{
    private readonly Queue<PlanningResult> _results = new(results);
    public List<SituationContext> Contexts { get; } = [];
    public Task<PlanningResult> PlanAsync(SituationContext context, CancellationToken cancellationToken)
    {
        Contexts.Add(context);
        return Task.FromResult(_results.Count == 0 ? new PlanningResult(null, "empty") : _results.Dequeue());
    }
}

sealed class FakeMouseBackend(MousePoint cursor, MousePoint target) : IMouseProbeBackend
{
    public nint CurrentGameWindowHandle => new(42);
    public MousePoint Cursor { get; private set; } = cursor;
    public int MoveCount { get; private set; }
    public bool IgnoreMovement { get; set; }
    public bool TryGetCursor(out MousePoint point, out string status) { point = Cursor; status = "ok"; return true; }
    public bool TryPrepareProbe(MousePoint original, out nint windowHandle, out MousePoint prepared, out string status)
    { windowHandle = CurrentGameWindowHandle; prepared = target; status = "ok"; return true; }
    public bool TrySetCursor(MousePoint point, out string status)
    { MoveCount++; if (!IgnoreMovement) Cursor = point; status = "ok"; return true; }
}
