using AgePilot.Core;
using AgePilot.Core.Automation;
using AgePilot.Core.Configuration;
using AgePilot.Core.History;
using AgePilot.Core.Observations;
using AgePilot.Core.Planning;
using AgePilot.Infrastructure.Planning;
using AgePilot.Vision.Geometry;
using AgePilot.Vision.Benchmarking;
using AgePilot.Vision.Observations;
using AgePilot.Vision.Ocr;
using AgePilot.Vision.Profiles;
using AgePilot.Vision.Images;
using OpenCvSharp;

var tests = new (string Name, Action Run)[]
{
    ("Population parser repairs OCR separators safely", PopulationParserRepairsSeparatorsSafely),
    ("Ambiguous compact population fails closed", AmbiguousCompactPopulationFailsClosed),
    ("Population OCR uses field-specific refinement", PopulationOcrUsesFieldSpecificRefinement),
    ("Repaired population requires two independent scans", RepairedPopulationRequiresTwoScans),
    ("Failed OCR is retried on the next frame", FailedOcrIsRetried),
    ("Medium confidence population is confirmed as a pair", MediumConfidencePopulationIsConfirmedAsPair),
    ("Conflicting population candidates remain unavailable", ConflictingPopulationRemainsUnavailable),
    ("Population-dependent actions fail closed", PopulationDependentActionsFailClosed),
    ("House pressure blocks villagers and allows houses", HousePressureSelectsHouse),
    ("Planner schema exposes only state-safe actions", PlannerSchemaUsesSafeActions),
    ("GamePlan contracts are versioned with scoped budgets", GamePlanContractsAreVersioned),
    ("Compact schema emits only the allowed decision scopes", CompactSchemaFollowsScope),
    ("Compact adapter preserves execution semantics deterministically", CompactAdapterIsDeterministic),
    ("Compact adapter rejects invalid enum and scope combinations", CompactAdapterFailsClosed),
    ("Crossing the house threshold requests Minor replanning", HouseThresholdTriggersMinorReplan),
    ("Live trace setting is removed", LiveTraceSettingIsRemoved),
    ("Legacy live trace setting is removed on load", LegacyLiveTraceSettingIsRemovedOnLoad),
    ("VLM presets are immutable and validated", VlmPresetsAreValidated),
    ("Battlefield ROI maps to the reviewed calibration rectangle", BattlefieldRoiMapsExactly),
    ("Battlefield composition preserves aspect ratio", BattlefieldCompositionPreservesAspectRatio),
    ("Golden ROI content stays perceptually stable", GoldenRoiContentStaysPerceptuallyStable),
    ("Panel hash separates stable attempted and accepted evidence", PanelHashSeparatesEvidence),
    ("Rejected panel request does not consume evidence", RejectedPanelRequestDoesNotConsumeEvidence),
    ("Visual encoding is lazy until planning requests it", VisualEncodingIsLazy),
    ("Visual composition failure keeps planning request pending", VisualCompositionFailureKeepsRequestPending),
    ("Screenshot manifest OCR remains exact", ScreenshotManifestOcrRemainsExact),
};

var failures = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception exception) { failures++; Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
return failures == 0 ? 0 : 1;

static void PopulationParserRepairsSeparatorsSafely()
{
    Equal(new PopulationValue(5, 5), PopulationTextParser.Parse("5/5"));
    Equal(new PopulationValue(5, 75), PopulationTextParser.Parse("5I75"));
    Equal(new PopulationValue(5, 100), PopulationTextParser.Parse("5|100"));
    Equal(new PopulationValue(5, 5), PopulationTextParser.Parse("515"));
    Equal(new PopulationValue(5, 15), PopulationTextParser.Parse("5115"));
    Equal(new PopulationValue(75, 100), PopulationTextParser.Parse("751100"));
    Equal(new PopulationValue(100, 200), PopulationTextParser.Parse("1001200"));
    Equal(new PopulationValue(5, 100), PopulationTextParser.Parse("5100"));
}

static void AmbiguousCompactPopulationFailsClosed()
{
    Equal<PopulationValue?>(null, PopulationTextParser.Parse("2234"));
    Equal<PopulationValue?>(null, PopulationTextParser.Parse("5/5/5"));
    Equal<PopulationValue?>(null, PopulationTextParser.Parse("5/501"));
    Equal<PopulationValue?>(null, PopulationTextParser.Parse("200/100"));
}

static void PopulationOcrUsesFieldSpecificRefinement()
{
    var engine = new RefiningFrameEngine();
    var result = Analyzer(engine).AnalyzeFrame(BlankFrame(), 2560, 1440, DateTimeOffset.UnixEpoch);
    Equal(new PopulationValue(5, 5), result.Population);
    Equal(1, engine.RefinementCount);
    Equal("5/5", result.Fields[HudField.Population].RawText);
}

static void RepairedPopulationRequiresTwoScans()
{
    var analyzer = Analyzer(new FrameSequenceEngine([
        FullFramePopulation(new("515", null, 0.97)),
        [new("515", null, 0.96)],
    ]));
    var estimator = new TemporalGameStateEstimator();
    var pixels = BlankFrame();
    var now = DateTimeOffset.UnixEpoch;
    var first = estimator.Update(analyzer.AnalyzeFrame(pixels, 2560, 1440, now), now);
    Equal(false, first.Population?.IsUsable == true);

    var populationRegion = new PixelRect(608, 15, 70, 36);
    pixels[(populationRegion.Y * 2560 + populationRegion.X) * 4] = 1;
    var secondAt = now.AddMilliseconds(500);
    var second = estimator.Update(analyzer.AnalyzeFrame(pixels, 2560, 1440, secondAt), secondAt);
    Equal(5, second.Population?.Value);
    Equal(5, second.PopulationCap?.Value);
    Equal(true, second.Population?.IsUsable == true && second.PopulationCap?.IsUsable == true);
}

static void FailedOcrIsRetried()
{
    var analyzer = Analyzer(new FrameSequenceEngine([
        FullFramePopulation(new("", null, 0.1)),
        [new("4/5", null, 0.95)],
    ]));
    var pixels = BlankFrame();
    var now = DateTimeOffset.UnixEpoch;
    var first = analyzer.AnalyzeFrame(pixels, 2560, 1440, now);
    Equal<PopulationValue?>(null, first.Population);
    Equal(7, analyzer.LastRecognizedRegionCount);

    var second = analyzer.AnalyzeFrame(pixels, 2560, 1440, now.AddMilliseconds(500));
    Equal(new PopulationValue(4, 5), second.Population);
    Equal(1, analyzer.LastRecognizedRegionCount);
}

static void MediumConfidencePopulationIsConfirmedAsPair()
{
    var analyzer = Analyzer(new FrameSequenceEngine([
        FullFramePopulation(new("4/5", null, 0.672)),
        [new("4/5", null, 0.672)],
    ]));
    var estimator = new TemporalGameStateEstimator();
    var pixels = BlankFrame();
    var now = DateTimeOffset.UnixEpoch;
    var first = estimator.Update(analyzer.AnalyzeFrame(pixels, 2560, 1440, now), now);
    Equal(false, first.Population?.IsUsable == true);
    Equal(false, first.PopulationCap?.IsUsable == true);

    var secondAt = now.AddMilliseconds(500);
    var second = estimator.Update(analyzer.AnalyzeFrame(pixels, 2560, 1440, secondAt), secondAt);
    Equal(4, second.Population?.Value);
    Equal(5, second.PopulationCap?.Value);
    Equal(true, second.Population?.IsUsable == true && second.PopulationCap?.IsUsable == true);

    _ = analyzer.AnalyzeFrame(pixels, 2560, 1440, now.AddMilliseconds(750));
    Equal(0, analyzer.LastRecognizedRegionCount);
}

static void ConflictingPopulationRemainsUnavailable()
{
    var estimator = new TemporalGameStateEstimator();
    var now = DateTimeOffset.UnixEpoch;
    var first = estimator.Update(RawPopulation("4/5", 0.6), now);
    var second = estimator.Update(RawPopulation("5/10", 0.6), now.AddMilliseconds(500));
    Equal(false, first.Population?.IsUsable == true);
    Equal(false, second.Population?.IsUsable == true);
    Equal(false, second.PopulationCap?.IsUsable == true);
}

static void PopulationDependentActionsFailClosed()
{
    var unknown = State(population: null, cap: null);
    Equal(false, GameActionRegistry.TryResolve(GameActionKind.QueueVillager, Bindings(), unknown, GameAge.Dark,
        out _, out var queueReason));
    Equal(true, queueReason.Contains("人口", StringComparison.Ordinal));
    Equal(false, GameActionRegistry.TryResolve(GameActionKind.BuildHouse, Bindings(), unknown, GameAge.Dark,
        out _, out var houseReason));
    Equal(true, houseReason.Contains("人口", StringComparison.Ordinal));
}

static void HousePressureSelectsHouse()
{
    var pressured = State(population: 18, cap: 20);
    Equal(false, GameActionRegistry.TryResolve(GameActionKind.QueueVillager, Bindings(), pressured, GameAge.Dark,
        out _, out _));
    Equal(true, GameActionRegistry.TryResolve(GameActionKind.BuildHouse, Bindings(), pressured, GameAge.Dark,
        out var house, out _));
    Equal(GameActionKind.BuildHouse, house.Kind);

    var safe = State(population: 17, cap: 20);
    Equal(true, GameActionRegistry.TryResolve(GameActionKind.QueueVillager, Bindings(), safe, GameAge.Dark,
        out _, out _));
}

static void PlannerSchemaUsesSafeActions()
{
    EqualSequence([GameActionKind.Observe, GameActionKind.Wait], LlamaServerPlanner.AllowedActions(State(null, null)));
    EqualSequence([GameActionKind.Observe, GameActionKind.Wait, GameActionKind.BuildHouse],
        LlamaServerPlanner.AllowedActions(State(18, 20)));
    EqualSequence([GameActionKind.Observe, GameActionKind.Wait, GameActionKind.QueueVillager],
        LlamaServerPlanner.AllowedActions(State(17, 20)));
}

static void HouseThresholdTriggersMinorReplan()
{
    var planner = new RecordingPlanner(Plan);
    using var engine = new StrategyEngine(planner);
    var history = new GameHistory();
    var now = DateTimeOffset.UnixEpoch;
    var safe = State(17, 20);
    history.Add(safe, now);
    _ = engine.UpdateAsync(safe, history, null, now, CancellationToken.None).Result;
    _ = engine.UpdateAsync(safe, history, null, now.AddMilliseconds(1), CancellationToken.None).Result;

    var pressured = State(18, 20);
    history.Add(pressured, now.AddSeconds(1));
    _ = engine.UpdateAsync(pressured, history, null, now.AddSeconds(1), CancellationToken.None).Result;
    Equal(2, planner.Contexts.Count);
    Equal(PlanUpdateScope.Minor, planner.Contexts[1].AllowedUpdateScope);
    Equal(true, planner.Contexts[1].RecentEvents.Any(item => item.Kind == "population_gate_changed"));
}

static void LiveTraceSettingIsRemoved() =>
    Equal<System.Reflection.PropertyInfo?>(null, typeof(AppSettings).GetProperty("EnableLiveTrace"));

static void LegacyLiveTraceSettingIsRemovedOnLoad()
{
    var directory = Path.Combine(Path.GetTempPath(), $"agepilot-settings-test-{Guid.NewGuid():N}");
    var path = Path.Combine(directory, "settings.json");
    Directory.CreateDirectory(directory);
    try
    {
        File.WriteAllText(path, "{\"EnableLiveTrace\":true,\"ScanIntervalMilliseconds\":500}");
        _ = new JsonSettingsStore(path).Load();
        Equal(false, File.ReadAllText(path).Contains("EnableLiveTrace", StringComparison.Ordinal));
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static void GamePlanContractsAreVersioned()
{
    Equal("legacy-v1", GamePlanContractCatalog.Legacy.Id);
    Equal("compact-v2", GamePlanContractCatalog.CompactV2.Id);
    Equal(64, GamePlanContractCatalog.CompactV2.CompletionBudgets[PlanUpdateScope.Minor].TargetMedian);
    Equal(96, GamePlanContractCatalog.CompactV2.CompletionBudgets[PlanUpdateScope.Major].TargetMedian);
    Equal(200, GamePlanContractCatalog.CompactV2.CompletionBudgets[PlanUpdateScope.Major].PromotionCeiling);
    Equal(256, GamePlanContractCatalog.CompactV2.CompletionBudgets[PlanUpdateScope.Major].HardCap);
}

static void CompactSchemaFollowsScope()
{
    var minor = System.Text.Json.JsonSerializer.Serialize(LlamaServerPlanner.BuildCompactResponseFormat(
        PlanUpdateScope.Minor, [GameActionKind.Observe]));
    Equal(true, minor.Contains("\"minor\"", StringComparison.Ordinal));
    Equal(false, minor.Contains("\"medium\":", StringComparison.Ordinal));
    Equal(false, minor.Contains("\"major\":", StringComparison.Ordinal));
    Equal(false, minor.Contains("assessment", StringComparison.Ordinal));
    Equal(false, minor.Contains("expectedResult", StringComparison.Ordinal));

    var major = System.Text.Json.JsonSerializer.Serialize(LlamaServerPlanner.BuildCompactResponseFormat(
        PlanUpdateScope.Major, [GameActionKind.BuildHouse]));
    Equal(true, major.Contains("\"medium\":", StringComparison.Ordinal));
    Equal(true, major.Contains("\"major\":", StringComparison.Ordinal));
    Equal(false, major.Contains("quantity", StringComparison.Ordinal));
}

static void CompactAdapterIsDeterministic()
{
    var now = DateTimeOffset.UnixEpoch.AddSeconds(10);
    var state = State(18, 20);
    var context = new SituationContext(state,
        GameHistorySummarizer.Summarize(new GameHistory(), TimeSpan.FromSeconds(1), now), null, null, [], now,
        Directive: new StrategyDirective("test", GameAge.Castle), AllowedUpdateScope: PlanUpdateScope.Major);
    var response = new CompactGamePlanResponse(GameActionKind.BuildHouse, .91, 1500,
        PlanReasonCode.PopulationCap, PlanScopeEscalation.None, MinorPlanIntent.PreventPopulationBlock,
        MediumPlanIntent.GrowEconomy, MajorPlanIntent.AdvanceAge);
    var first = CompactGamePlanAdapter.Adapt(response, context, PlanUpdateScope.Major, now);
    var second = CompactGamePlanAdapter.Adapt(response, context, PlanUpdateScope.Major, now.AddMilliseconds(1));
    Equal(true, first.Success);
    Equal(GameActionKind.BuildHouse, first.Plan!.VisualDecision!.Action.Kind);
    Equal(1, first.Plan.VisualDecision.Action.Quantity);
    Equal(first.Plan.MajorDecision!.NodeId, second.Plan!.MajorDecision!.NodeId);
    Equal(first.Plan.MediumDecision!.NodeId, second.Plan.MediumDecision!.NodeId);
    Equal(first.Plan.MinorDecision!.NodeId, second.Plan.MinorDecision!.NodeId);
    Equal(true, first.Plan.MinorDecision.Evidence.Contains("18/20", StringComparison.Ordinal));
}

static void CompactAdapterFailsClosed()
{
    var now = DateTimeOffset.UnixEpoch.AddSeconds(10);
    var previous = Plan(now);
    var context = new SituationContext(State(17, 20),
        GameHistorySummarizer.Summarize(new GameHistory(), TimeSpan.FromSeconds(1), now), null, previous, [], now,
        Directive: new StrategyDirective("test", GameAge.Castle), AllowedUpdateScope: PlanUpdateScope.Minor);
    var unknown = new CompactGamePlanResponse(GameActionKind.QueueVillager, .9, 1000,
        (PlanReasonCode)999, PlanScopeEscalation.None, MinorPlanIntent.MaintainVillagerProduction);
    Equal(false, CompactGamePlanAdapter.Adapt(unknown, context, PlanUpdateScope.Minor, now).Success);
    var wrongScope = new CompactGamePlanResponse(GameActionKind.QueueVillager, .9, 1000,
        PlanReasonCode.EconomyGrowth, PlanScopeEscalation.None, MinorPlanIntent.MaintainVillagerProduction,
        MediumPlanIntent.GrowEconomy);
    Equal(false, CompactGamePlanAdapter.Adapt(wrongScope, context, PlanUpdateScope.Minor, now).Success);
    var nonRaising = wrongScope with { Medium = null, Raise = PlanScopeEscalation.Medium };
    Equal(true, CompactGamePlanAdapter.Adapt(nonRaising, context, PlanUpdateScope.Minor, now).Success);
    var contradiction = nonRaising with { Raise = PlanScopeEscalation.None, Minor = MinorPlanIntent.RecoverObservation };
    Equal(false, CompactGamePlanAdapter.Adapt(contradiction, context, PlanUpdateScope.Minor, now).Success);
    var waitForHouse = new CompactGamePlanResponse(GameActionKind.BuildHouse, .9, 1000,
        PlanReasonCode.PopulationCap, PlanScopeEscalation.None, MinorPlanIntent.WaitForOutcome);
    var houseContext = context with { State = State(18, 20) };
    Equal(true, CompactGamePlanAdapter.Adapt(waitForHouse, houseContext, PlanUpdateScope.Minor, now).Success);
}

static void VlmPresetsAreValidated()
{
    Equal("legacy-3-1024-v1", VlmPipelinePresetCatalog.Legacy.Id);
    Equal(640, VlmPipelinePresetCatalog.Get("event-panel-640-v1").ImageMaxTokens);
    var failed = false;
    try { _ = VlmPipelinePresetCatalog.Get("event-panel-custom"); }
    catch (InvalidDataException) { failed = true; }
    Equal(true, failed);
}

static void BattlefieldRoiMapsExactly()
{
    var profile = HudProfileLoader.Load(Path.Combine(RepositoryRoot(), "config", "hud", "aoe2de-zh-tw-2560x1440-50.json"));
    Equal(new PixelRect(0, 64, 2560, 887), profile.BattlefieldRegion!.Value.ToPixels(2560, 1440));
    Equal(new PixelRect(0, 950, 1204, 490), profile.CommandPanelRegion!.Value.ToPixels(2560, 1440));
    Equal(new PixelRect(2062, 971, 482, 457), profile.MinimapRegion!.Value.ToPixels(2560, 1440));
}

static void BattlefieldCompositionPreservesAspectRatio()
{
    var root = RepositoryRoot();
    var profile = HudProfileLoader.Load(Path.Combine(root, "config", "hud", "aoe2de-zh-tw-2560x1440-50.json"));
    var source = BgraImageLoader.Load(Path.Combine(root, "doc", "Snipaste_2026-08-09_16-29-15.jpg"));
    var result = VisualPromptImageEncoder.Compose(source.Pixels, source.Width, source.Height,
        VlmPipelinePresetCatalog.Get("event-panel-edge1280-v1"), profile.BattlefieldRegion!.Value,
        profile.CommandPanelRegion!.Value, profile.MinimapRegion!.Value, includePanel: false, []);
    EqualSequence(["battlefield", "minimap"], result.Images.Select(image => image.Name).ToArray());
    Equal(1280, result.Images[0].Width);
    Equal(444, result.Images[0].Height);
    Equal(482, result.Images[1].Width);
    Equal(457, result.Images[1].Height);
}

static void GoldenRoiContentStaysPerceptuallyStable()
{
    var root = RepositoryRoot();
    var profile = HudProfileLoader.Load(Path.Combine(root, "config", "hud", "aoe2de-zh-tw-2560x1440-50.json"));
    var source = BgraImageLoader.Load(Path.Combine(root, "doc", "Snipaste_2026-08-09_16-29-15.jpg"));
    var result = VisualPromptImageEncoder.Compose(source.Pixels, source.Width, source.Height,
        VlmPipelinePresetCatalog.Get("battlefield-3-1024-v1"), profile.BattlefieldRegion!.Value,
        profile.CommandPanelRegion!.Value, profile.MinimapRegion!.Value, includePanel: true, ["golden"]);
    var expected = new Dictionary<string, ulong>
    {
        ["battlefield"] = 0x41A6BCF9C3DB0E24,
        ["command_panel"] = 0x74ECCAA73CA4C25A,
        ["minimap"] = 0x53B8A842FF472C3A,
    };
    foreach (var image in result.Images)
    {
        using var decoded = Cv2.ImDecode(image.Data, ImreadModes.Color);
        var actual = PanelHashTracker.ComputePerceptualHash(decoded);
        var distance = PanelHashTracker.HammingDistance(expected[image.Name], actual);
        if (distance > 2) throw new InvalidOperationException($"{image.Name} golden pHash distance {distance} exceeds 2.");
    }
}

static void PanelHashSeparatesEvidence()
{
    var tracker = new PanelHashTracker(10, 2, TimeSpan.FromMilliseconds(750), 2);
    var start = DateTimeOffset.UnixEpoch;
    tracker.ObserveHash(0, start);
    tracker.ObserveHash(0, start.AddMilliseconds(800));
    Equal(true, tracker.Snapshot.PanelDirty);
    tracker.MarkAttempted(0, start.AddMilliseconds(810));
    Equal(true, tracker.Snapshot.PanelDirty);
    tracker.MarkAccepted(0, start.AddMilliseconds(900));
    Equal(false, tracker.Snapshot.PanelDirty);

    tracker.ObserveHash(0x3ff, start.AddMilliseconds(1000));
    tracker.ObserveHash(0x3ff, start.AddMilliseconds(1800));
    Equal(true, tracker.Snapshot.PanelDirty);
    tracker.MarkAccepted(0, start.AddMilliseconds(1900));
    Equal(true, tracker.Snapshot.PanelDirty);
}

static void RejectedPanelRequestDoesNotConsumeEvidence()
{
    var root = RepositoryRoot();
    var profile = HudProfileLoader.Load(Path.Combine(root, "config", "hud", "aoe2de-zh-tw-2560x1440-50.json"));
    var preset = VlmPipelinePresetCatalog.Get("event-panel-640-v1");
    var now = DateTimeOffset.UnixEpoch.AddSeconds(1);
    var composer = new VisualPromptComposer(preset, profile, () => now);
    composer.ObservePanelHash(0, DateTimeOffset.UnixEpoch);
    composer.ObservePanelHash(0, DateTimeOffset.UnixEpoch.AddMilliseconds(800));
    var pixels = BlankFrame();
    var first = composer.Compose(pixels, 2560, 1440,
        new VisualRequestContext(PlanUpdateScope.Major, [], now), "test", null, null);
    Equal(true, first.Observation.Images.Any(image => image.Name == "command_panel"));
    first.Complete(false);
    Equal<ulong?>(null, composer.PanelState.LastAcceptedPanelHash);

    now = now.AddMilliseconds(100);
    var retry = composer.Compose(pixels, 2560, 1440,
        new VisualRequestContext(PlanUpdateScope.Minor, [], now), "test", null, null);
    EqualSequence(["bootstrap", "dirty"], retry.Observation.Telemetry!.PanelInclusionReasons);
    retry.Complete(true);
    Equal<ulong?>(0, composer.PanelState.LastAcceptedPanelHash);
}

static void VisualEncodingIsLazy()
{
    var planner = new RecordingPlanner(Plan);
    using var engine = new StrategyEngine(planner);
    var now = DateTimeOffset.UnixEpoch;
    var state = State(17, 20);
    var history = new GameHistory();
    history.Add(state, now);
    var calls = 0;
    var lease = new TrackingVisualLease();
    _ = engine.UpdateAsync(state, history, null, now, CancellationToken.None,
        visualFactory: _ => { calls++; return lease; }).Result;
    Equal(1, calls);
    _ = engine.UpdateAsync(state, history, null, now.AddMilliseconds(1), CancellationToken.None,
        visualFactory: _ => { calls++; return new TrackingVisualLease(); }).Result;
    Equal(1, calls);
    Equal(true, lease.Accepted);
}

static void VisualCompositionFailureKeepsRequestPending()
{
    var planner = new RecordingPlanner(Plan);
    using var engine = new StrategyEngine(planner);
    var now = DateTimeOffset.UnixEpoch;
    var state = State(17, 20);
    var history = new GameHistory();
    history.Add(state, now);
    var failed = false;
    try
    {
        _ = engine.UpdateAsync(state, history, null, now, CancellationToken.None,
            visualFactory: _ => throw new InvalidDataException("encode failed")).Result;
    }
    catch (InvalidDataException) { failed = true; }
    Equal(true, failed);
    _ = engine.UpdateAsync(state, history, null, now.AddMilliseconds(1), CancellationToken.None,
        visualFactory: _ => new TrackingVisualLease()).Result;
    Equal(1, planner.Contexts.Count);
}

static void ScreenshotManifestOcrRemainsExact()
{
    var report = VisionBenchmarkRunner.Run(Path.Combine(RepositoryRoot(), "testdata", "screenshots", "manifest.json"));
    Equal(true, report.Samples.All(sample => sample.FrameExact));
}

static AdaptiveHudOcrAnalyzer Analyzer(IFrameOcrEngine engine) => new(engine,
    HudProfileLoader.Load(Path.Combine(RepositoryRoot(), "config", "hud", "aoe2de-zh-tw-2560x1440-50.json")));

static byte[] BlankFrame() => new byte[2560 * 1440 * 4];

static OcrResult[] FullFramePopulation(OcrResult population) =>
[
    new("200", 200, 0.99), new("200", 200, 0.99), new("100", 100, 0.99), new("200", 200, 0.99),
    population, new("黑暗時代", null, 0.99), new("", null, 0),
];

static HudOcrResult RawPopulation(string text, double confidence)
{
    var parsed = PopulationTextParser.Parse(text);
    var fields = new Dictionary<HudField, OcrResult>
    {
        [HudField.Wood] = new("200", 200, 0.99), [HudField.Food] = new("200", 200, 0.99),
        [HudField.Gold] = new("100", 100, 0.99), [HudField.Stone] = new("200", 200, 0.99),
        [HudField.Population] = new(text, null, confidence),
    };
    return new(fields, parsed);
}

static GameState State(int? population, int? cap) => new()
{
    Age = GameAge.Dark,
    Food = Confirmed(200), Wood = Confirmed(200), Gold = Confirmed(100), Stone = Confirmed(200),
    Population = population is { } current ? Confirmed(current) : ObservedValue<int>.Unavailable(DateTimeOffset.UnixEpoch),
    PopulationCap = cap is { } maximum ? Confirmed(maximum) : ObservedValue<int>.Unavailable(DateTimeOffset.UnixEpoch),
};

static ObservedValue<int> Confirmed(int value) =>
    new(value, 0.99, DateTimeOffset.UnixEpoch, ObservationStatus.Confirmed);

static GameHotKeyBindings Bindings() => new()
{
    Id = "test", Verified = true,
    Keys = new()
    {
        ["selectTownCenter"] = "H", ["queueVillager"] = "Q", ["selectIdleVillager"] = ".",
        ["openEconomicBuildings"] = "Q", ["buildHouse"] = "Q",
    },
};

static string RepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgePilot.sln"))) directory = directory.Parent;
    return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
}

static GamePlan Plan(DateTimeOffset now) => new(
    Guid.NewGuid().ToString("N"), now, now.AddMinutes(1), "test", "test", "test", 0.9,
    VisualDecision: new VisualPlayerDecision("test", "test", "test", new GameAction(GameActionKind.Wait), "none", 500, 0.9),
    MajorDecision: new DecisionNode("major", DecisionLevel.Major, "test", "test", "test", "test", "test", DecisionStatus.Active),
    MediumDecision: new DecisionNode("medium", DecisionLevel.Medium, "test", "test", "test", "test", "test", DecisionStatus.Active),
    MinorDecision: new DecisionNode(Guid.NewGuid().ToString("N"), DecisionLevel.Minor, "test", "test", "test", "test", "test", DecisionStatus.Active));

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
}

static void EqualSequence<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException($"Expected [{string.Join(",", expected)}], got [{string.Join(",", actual)}].");
}

sealed class FrameSequenceEngine(IEnumerable<IReadOnlyList<OcrResult>> frames) : IFrameOcrEngine
{
    private readonly Queue<IReadOnlyList<OcrResult>> _frames = new(frames);
    public IReadOnlyList<OcrResult> RecognizeFrame(ReadOnlyMemory<byte> bgraPixels, int frameWidth, int frameHeight,
        IReadOnlyList<PixelRect> regions)
    {
        var result = _frames.Dequeue();
        if (result.Count != regions.Count) throw new InvalidOperationException($"Fake OCR expected {result.Count} regions, got {regions.Count}.");
        return result;
    }
}

sealed class RefiningFrameEngine : IFrameOcrEngine, IPopulationOcrEngine
{
    public int RefinementCount { get; private set; }

    public IReadOnlyList<OcrResult> RecognizeFrame(ReadOnlyMemory<byte> bgraPixels, int frameWidth, int frameHeight,
        IReadOnlyList<PixelRect> regions) => FullFramePopulation(new("515", null, 0.97));

    public OcrResult RefinePopulation(ReadOnlyMemory<byte> bgraPixels, int frameWidth, int frameHeight,
        PixelRect region, OcrResult baseline)
    {
        RefinementCount++;
        return new("5/5", null, 0.8);
    }

    private static OcrResult[] FullFramePopulation(OcrResult population) =>
    [
        new("200", 200, 0.99), new("200", 200, 0.99), new("100", 100, 0.99), new("200", 200, 0.99),
        population, new("Dark Age", null, 0.99), new("", null, 0),
    ];
}

sealed class RecordingPlanner(Func<DateTimeOffset, GamePlan> planFactory) : IStrategicPlanner
{
    public List<SituationContext> Contexts { get; } = [];
    public Task<PlanningResult> PlanAsync(SituationContext context, CancellationToken cancellationToken)
    {
        Contexts.Add(context);
        return Task.FromResult(new PlanningResult(planFactory(context.CapturedAt), null));
    }
}

sealed class TrackingVisualLease : IVisualRequestLease
{
    public VisualObservation Observation { get; } = new(2560, 1440, [], "test", null, null);
    public bool? Accepted { get; private set; }
    public void Complete(bool accepted) => Accepted = accepted;
}
