using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgePilot.Core;
using AgePilot.Core.Automation;
using AgePilot.Core.Configuration;
using AgePilot.Core.History;
using AgePilot.Core.Observations;
using AgePilot.Core.Planning;
using AgePilot.Infrastructure.Planning;
using AgePilot.Vision.Images;
using AgePilot.Vision.Profiles;

namespace AgePilot.App;

public static class VlmPipelineBenchmark
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<VlmBenchmarkReport> RunAsync(
        string manifestPath, string outputPath, CancellationToken cancellationToken)
    {
        var manifest = JsonSerializer.Deserialize<VlmBenchmarkManifest>(File.ReadAllText(manifestPath), JsonOptions)
            ?? throw new InvalidDataException("VLM benchmark manifest 無效。");
        var root = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        var profile = HudProfileLoader.Load(Resolve(root, manifest.Profile));
        var sequenceResults = RunSequences(manifest.Sequences, root, profile);
        var runs = new List<VlmSnapshotRun>();
        var presetIds = manifest.Presets.Count == 0
            ? VlmPipelinePresetCatalog.All.Select(item => item.Id).ToArray()
            : manifest.Presets;

        foreach (var presetId in presetIds)
        {
            var preset = VlmPipelinePresetCatalog.Get(presetId);
            var settings = JsonSettingsStore.CreateDefault().Load();
            settings.VlmPipelinePresetId = preset.Id;
            settings.LlmSeed = 42;
            settings.Validate();
            using var planner = new LlamaServerPlanner(settings);
            var composer = new VisualPromptComposer(preset, profile);
            var warmed = false;
            foreach (var sample in manifest.Snapshots)
            {
                var image = BgraImageLoader.Load(Resolve(root, sample.Image));
                for (var repetition = warmed ? 1 : 0; repetition <= 3; repetition++)
                {
                    var now = DateTimeOffset.UtcNow;
                    composer.ObserveFrame(image.Pixels, image.Width, image.Height, now.AddSeconds(-1));
                    composer.ObserveFrame(image.Pixels, image.Width, image.Height, now);
                    var requestContext = new VisualRequestContext(PlanUpdateScope.Major,
                        [new PlanningEvent("benchmark_snapshot", sample.Id, now, PlanUpdateScope.Major)], now);
                    var lease = composer.Compose(image.Pixels, image.Width, image.Height, requestContext,
                        "battlefield=純世界視野；minimap=右下小地圖；command_panel=條件式左下指令面板",
                        null, null);
                    var state = ToState(sample.State, now);
                    var context = new SituationContext(state,
                        GameHistorySummarizer.Summarize(new GameHistory(), TimeSpan.FromSeconds(1), now),
                        null, null, requestContext.Events, now, lease.Observation,
                        new StrategyDirective("benchmark", sample.TargetAge), PlanUpdateScope.Major);
                    var clock = Stopwatch.StartNew();
                    var result = await planner.PlanAsync(context, cancellationToken);
                    clock.Stop();
                    lease.Complete(result.Success);
                    if (!warmed)
                    {
                        warmed = true;
                        continue;
                    }
                    var action = result.Plan?.VisualDecision?.Action.Kind;
                    var quality = result.Success &&
                        (sample.AllowedActions.Count == 0 || action is { } allowed && sample.AllowedActions.Contains(allowed)) &&
                        (action is null || !sample.ForbiddenActions.Contains(action.Value));
                    runs.Add(new(sample.Id, preset.Id, preset.Revision, repetition,
                        clock.ElapsedMilliseconds, lease.Observation.Images.Count,
                        lease.Observation.Telemetry?.PanelInclusionReasons ?? [], action, result.Success,
                        quality, result.Error, planner.LastTelemetry));
                }
            }
        }

        var coveredTags = manifest.Snapshots.SelectMany(item => item.Tags)
            .Concat(manifest.Sequences.SelectMany(item => item.Tags)).ToHashSet(StringComparer.Ordinal);
        var missingCoverage = manifest.RequiredCoverageTags.Where(tag => !coveredTags.Contains(tag)).ToArray();
        var summaries = BuildSummaries(runs);
        var report = new VlmBenchmarkReport(1, DateTimeOffset.UtcNow, manifestPath, runs,
            sequenceResults, summaries, missingCoverage, EvaluatePromotion(summaries, missingCoverage.Length == 0));
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(report, JsonOptions));
        return report;
    }

    public static IReadOnlyList<VlmSequenceResult> RunSequenceOnly(string manifestPath)
    {
        var manifest = JsonSerializer.Deserialize<VlmBenchmarkManifest>(File.ReadAllText(manifestPath), JsonOptions)
            ?? throw new InvalidDataException("VLM benchmark manifest 無效。");
        var root = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        var profile = HudProfileLoader.Load(Resolve(root, manifest.Profile));
        return RunSequences(manifest.Sequences, root, profile);
    }

    public static void WriteSequenceReport(IReadOnlyList<VlmSequenceResult> report, string outputPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(report, JsonOptions));
    }

    private static IReadOnlyList<VlmSequenceResult> RunSequences(
        IReadOnlyList<VlmSequenceCase> cases, string root, HudProfile profile)
    {
        var results = new List<VlmSequenceResult>();
        foreach (var item in cases)
        {
            var now = DateTimeOffset.UnixEpoch;
            var preset = VlmPipelinePresetCatalog.Get(item.PresetId);
            var composer = new VisualPromptComposer(preset, profile, () => now);
            var leases = new Dictionary<string, (IVisualRequestLease Lease, bool IncludedPanel)>(StringComparer.Ordinal);
            BgraImage? current = null;
            var failures = new List<string>();
            var totalRequests = 0;
            var includedPanelRequests = 0;
            var acceptedPanelRequests = 0;
            var retryAfterFailureRequests = 0;
            var retryRequired = false;
            var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
            DateTimeOffset? dirtySince = null;
            var dirtyToIncluded = new List<double>();
            foreach (var step in item.Steps.OrderBy(step => step.AtMs))
            {
                now = DateTimeOffset.UnixEpoch.AddMilliseconds(step.AtMs);
                if (step.Kind == "frame")
                {
                    if (string.IsNullOrWhiteSpace(step.Frame)) { failures.Add($"{step.AtMs}: frame path missing"); continue; }
                    current = BgraImageLoader.Load(Resolve(root, step.Frame));
                    composer.ObserveFrame(current.Pixels, current.Width, current.Height, now);
                }
                else if (step.Kind == "panel-hash")
                {
                    if (string.IsNullOrWhiteSpace(step.RawHash) ||
                        !ulong.TryParse(step.RawHash, System.Globalization.NumberStyles.HexNumber, null, out var hash))
                    { failures.Add($"{step.AtMs}: invalid raw hash"); continue; }
                    composer.ObservePanelHash(hash, now);
                }
                else if (step.Kind == "request-start")
                {
                    if (current is null) { failures.Add($"{step.AtMs}: request has no frame"); continue; }
                    var events = string.IsNullOrWhiteSpace(step.EventKind) ? [] : new[]
                    {
                        new PlanningEvent(step.EventKind, step.EventDetail ?? "", now),
                    };
                    var lease = composer.Compose(current.Pixels, current.Width, current.Height,
                        new VisualRequestContext(PlanUpdateScope.Minor, events, now), "sequence", null, null);
                    var included = lease.Observation.Images.Any(image => image.Name == "command_panel");
                    leases[step.RequestId ?? "default"] = (lease, included);
                    totalRequests++;
                    if (included)
                    {
                        includedPanelRequests++;
                        if (retryRequired) retryAfterFailureRequests++;
                        if (dirtySince is { } started && lease.Observation.Telemetry?.PanelInclusionReasons.Contains("dirty") == true)
                            dirtyToIncluded.Add((now - started).TotalMilliseconds);
                        foreach (var reason in lease.Observation.Telemetry?.PanelInclusionReasons ?? [])
                            reasons[reason] = reasons.GetValueOrDefault(reason) + 1;
                    }
                    Check(step.ExpectedPanelIncluded, included,
                        failures, step.AtMs, "panel included");
                    if (step.ExpectedInclusionReasons is { Count: > 0 })
                        CheckSequence(step.ExpectedInclusionReasons,
                            lease.Observation.Telemetry?.PanelInclusionReasons ?? [], failures, step.AtMs, "inclusion reasons");
                }
                else if (step.Kind == "request-complete")
                {
                    var key = step.RequestId ?? "default";
                    if (!leases.Remove(key, out var pending)) { failures.Add($"{step.AtMs}: unknown request {key}"); continue; }
                    var accepted = step.Outcome == "accepted";
                    pending.Lease.Complete(accepted);
                    if (pending.IncludedPanel && accepted) acceptedPanelRequests++;
                    retryRequired = pending.IncludedPanel && !accepted;
                }
                else failures.Add($"{step.AtMs}: unknown kind {step.Kind}");

                var state = composer.PanelState;
                if (state.PanelDirty && dirtySince is null) dirtySince = now;
                else if (!state.PanelDirty) dirtySince = null;
                Check(step.ExpectedPanelDirty, state.PanelDirty, failures, step.AtMs, "panel dirty");
                if (step.ExpectedLastAcceptedHash is { } expectedHash)
                    Check(expectedHash, FormatHash(state.LastAcceptedPanelHash), failures, step.AtMs, "last accepted hash");
            }
            results.Add(new(item.Id, preset.Id, failures.Count == 0, failures, composer.PanelState,
                totalRequests, includedPanelRequests, acceptedPanelRequests, retryAfterFailureRequests,
                totalRequests == 0 ? 0 : includedPanelRequests / (double)totalRequests,
                includedPanelRequests == 0 ? 0 : acceptedPanelRequests / (double)includedPanelRequests,
                dirtyToIncluded.Count == 0 ? null : Median(dirtyToIncluded), reasons));
        }
        return results;
    }

    private static void Check<T>(T? expected, T actual, List<string> failures, long atMs, string name)
    {
        if (expected is not null && !EqualityComparer<T>.Default.Equals(expected, actual))
            failures.Add($"{atMs}: expected {name}={expected}, actual={actual}");
    }

    private static void CheckSequence<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual,
        List<string> failures, long atMs, string name)
    {
        if (!expected.SequenceEqual(actual)) failures.Add($"{atMs}: {name} mismatch");
    }

    private static string? FormatHash(ulong? value) => value is { } hash ? hash.ToString("X16") : null;

    private static GameState ToState(VlmBenchmarkState state, DateTimeOffset at) => new()
    {
        Age = state.Age,
        Food = Observed(state.Food, at), Wood = Observed(state.Wood, at),
        Gold = Observed(state.Gold, at), Stone = Observed(state.Stone, at),
        Population = Observed(state.Population, at), PopulationCap = Observed(state.PopulationCap, at),
    };

    private static ObservedValue<int> Observed(int? value, DateTimeOffset at) => value is { } actual
        ? new(actual, 0.99, at, ObservationStatus.Confirmed)
        : ObservedValue<int>.Unavailable(at);

    private static IReadOnlyList<VlmPresetSummary> BuildSummaries(IReadOnlyList<VlmSnapshotRun> runs) => runs
        .GroupBy(run => run.PresetId)
        .Select(group =>
        {
            var successful = group.Where(run => run.Success).ToArray();
            var latencies = successful.Select(run => (double)run.EndToEndMilliseconds).Order().ToArray();
            var promptTokens = successful.Where(run => run.Telemetry?.PromptTokens is not null)
                .Select(run => (double)run.Telemetry!.PromptTokens!.Value).Order().ToArray();
            double? medianPromptTokens = promptTokens.Length == 0 ? null : Median(promptTokens);
            var byImageCount = successful.GroupBy(run => run.ImageCount).ToDictionary(
                bucket => bucket.Key,
                bucket => new LatencySummary(Median(bucket.Select(run => (double)run.EndToEndMilliseconds)),
                    Percentile(bucket.Select(run => (double)run.EndToEndMilliseconds), 0.95)));
            var byReason = successful.SelectMany(run =>
                    (run.InclusionReasons.Count == 0
                        ? new[] { run.ImageCount == 2 ? "two-images" : "always-panel" }
                        : run.InclusionReasons).Select(reason => (reason, run.EndToEndMilliseconds)))
                .GroupBy(item => item.reason).ToDictionary(bucket => bucket.Key,
                    bucket => new LatencySummary(Median(bucket.Select(item => (double)item.EndToEndMilliseconds)),
                        Percentile(bucket.Select(item => (double)item.EndToEndMilliseconds), 0.95)));
            var scenarioMedians = successful.GroupBy(run => run.CaseId)
                .Select(bucket => Median(bucket.Select(run => (double)run.EndToEndMilliseconds))).ToArray();
            var panelIncludedRuns = successful.Count(run => run.ImageCount == 3);
            return new VlmPresetSummary(group.Key, group.First().PresetRevision, group.Count(), successful.Length,
                group.Count(run => run.QualityPassed), Median(latencies), Percentile(latencies, 0.95),
                Median(scenarioMedians), Percentile(scenarioMedians, 0.95), medianPromptTokens,
                panelIncludedRuns, successful.Length == 0 ? 0 : panelIncludedRuns / (double)successful.Length,
                byImageCount, byReason);
        }).ToArray();

    private static IReadOnlyList<VlmPromotionResult> EvaluatePromotion(IReadOnlyList<VlmPresetSummary> summaries, bool coverageComplete)
    {
        var baseline = summaries.FirstOrDefault(item => item.PresetId == VlmPipelinePresetCatalog.Legacy.Id);
        if (baseline is null) return [];
        return summaries.Where(item => item.PresetId != baseline.PresetId).Select(candidate =>
        {
            var quality = coverageComplete && candidate.SuccessfulRuns == candidate.TotalRuns && candidate.QualityPassedRuns == candidate.TotalRuns;
            var medianGate = candidate.GlobalMedianMilliseconds <= Math.Min(6000, baseline.GlobalMedianMilliseconds * 0.65);
            var p95Gate = candidate.GlobalP95Milliseconds <= Math.Min(12000, baseline.GlobalP95Milliseconds);
            var tokenGate = candidate.MedianPromptTokens is { } tokens && baseline.MedianPromptTokens is { } baselineTokens && tokens < baselineTokens;
            return new VlmPromotionResult(candidate.PresetId, quality, medianGate, p95Gate, tokenGate,
                quality && medianGate && p95Gate && tokenGate);
        }).ToArray();
    }

    private static double Median(IEnumerable<double> source) => Percentile(source, 0.5);
    private static double Percentile(IEnumerable<double> source, double percentile)
    {
        var values = source.Order().ToArray();
        if (values.Length == 0) return 0;
        return values[Math.Clamp((int)Math.Ceiling(values.Length * percentile) - 1, 0, values.Length - 1)];
    }

    private static string Resolve(string root, string path) => Path.IsPathRooted(path) ? path : Path.GetFullPath(path, root);
}

public sealed record VlmBenchmarkManifest(
    int SchemaVersion,
    string Profile,
    IReadOnlyList<string> Presets,
    IReadOnlyList<string> RequiredCoverageTags,
    IReadOnlyList<VlmSnapshotCase> Snapshots,
    IReadOnlyList<VlmSequenceCase> Sequences);

public sealed record VlmSnapshotCase(
    string Id,
    string Image,
    VlmBenchmarkState State,
    IReadOnlyList<GameActionKind> AllowedActions,
    IReadOnlyList<GameActionKind> ForbiddenActions,
    IReadOnlyList<string> Tags,
    GameAge TargetAge = GameAge.Castle);

public sealed record VlmBenchmarkState(GameAge? Age, int? Food, int? Wood, int? Gold, int? Stone,
    int? Population, int? PopulationCap);

public sealed record VlmSequenceCase(string Id, string PresetId, IReadOnlyList<string> Tags,
    IReadOnlyList<VlmSequenceStep> Steps);

public sealed record VlmSequenceStep(
    long AtMs,
    string Kind,
    string? Frame = null,
    string? RawHash = null,
    string? RequestId = null,
    string? Outcome = null,
    string? EventKind = null,
    string? EventDetail = null,
    bool? ExpectedPanelDirty = null,
    bool? ExpectedPanelIncluded = null,
    IReadOnlyList<string>? ExpectedInclusionReasons = null,
    string? ExpectedLastAcceptedHash = null);

public sealed record VlmSnapshotRun(
    string CaseId, string PresetId, int PresetRevision, int Repetition, long EndToEndMilliseconds,
    int ImageCount, IReadOnlyList<string> InclusionReasons, GameActionKind? Action, bool Success,
    bool QualityPassed, string? Error, PlannerRequestTelemetry? Telemetry);

public sealed record VlmSequenceResult(string CaseId, string PresetId, bool Passed,
    IReadOnlyList<string> Failures, PanelHashSnapshot FinalState,
    int TotalRequests, int PanelIncludedRequests, int AcceptedPanelRequests, int RetryAfterFailureRequests,
    double PanelInclusionShare, double AttemptedToAcceptedRatio, double? MedianDirtyToIncludedMilliseconds,
    IReadOnlyDictionary<string, int> InclusionReasonCounts);

public sealed record LatencySummary(double MedianMilliseconds, double P95Milliseconds);

public sealed record VlmPresetSummary(
    string PresetId, int PresetRevision, int TotalRuns, int SuccessfulRuns, int QualityPassedRuns,
    double GlobalMedianMilliseconds, double GlobalP95Milliseconds,
    double ScenarioMedianMilliseconds, double ScenarioP95Milliseconds, double? MedianPromptTokens,
    int PanelIncludedRuns, double PanelInclusionShare,
    IReadOnlyDictionary<int, LatencySummary> LatencyByImageCount,
    IReadOnlyDictionary<string, LatencySummary> LatencyByInclusionReason);

public sealed record VlmPromotionResult(string PresetId, bool QualityGate, bool MedianGate,
    bool P95Gate, bool TokenGate, bool Eligible);

public sealed record VlmBenchmarkReport(
    int SchemaVersion, DateTimeOffset GeneratedAt, string ManifestPath,
    IReadOnlyList<VlmSnapshotRun> SnapshotRuns,
    IReadOnlyList<VlmSequenceResult> SequenceResults,
    IReadOnlyList<VlmPresetSummary> PresetSummaries,
    IReadOnlyList<string> MissingCoverageTags,
    IReadOnlyList<VlmPromotionResult> PromotionResults);
