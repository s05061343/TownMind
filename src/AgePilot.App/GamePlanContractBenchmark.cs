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

/// <summary>Paired contract-only benchmark. Image composition is always legacy-3-1024-v1.</summary>
public static class GamePlanContractBenchmark
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<GamePlanContractBenchmarkReport> RunAsync(
        string manifestPath, string outputPath, CancellationToken cancellationToken)
    {
        var manifest = JsonSerializer.Deserialize<VlmBenchmarkManifest>(File.ReadAllText(manifestPath), JsonOptions)
            ?? throw new InvalidDataException("VLM benchmark manifest 無效。");
        var root = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        var profile = HudProfileLoader.Load(Resolve(root, manifest.Profile));
        var runs = new List<GamePlanContractRun>();

        foreach (var contract in GamePlanContractCatalog.All)
        {
            var settings = JsonSettingsStore.CreateDefault().Load();
            settings.VlmPipelinePresetId = VlmPipelinePresetCatalog.Legacy.Id;
            settings.GamePlanContractId = contract.Id;
            settings.LlmSeed = 42;
            settings.Validate();
            using var planner = new LlamaServerPlanner(settings);
            var warmed = false;
            foreach (var sample in manifest.Snapshots)
            {
                var image = BgraImageLoader.Load(Resolve(root, sample.Image));
                foreach (var scope in Enum.GetValues<PlanUpdateScope>().OrderByDescending(item => item))
                {
                    for (var repetition = warmed ? 1 : 0; repetition <= 3; repetition++)
                    {
                        var now = DateTimeOffset.UtcNow;
                        var composer = new VisualPromptComposer(VlmPipelinePresetCatalog.Legacy, profile);
                        composer.ObserveFrame(image.Pixels, image.Width, image.Height, now);
                        var visualContext = new VisualRequestContext(scope,
                            [new PlanningEvent("contract_benchmark", sample.Id, now, scope)], now);
                        var lease = composer.Compose(image.Pixels, image.Width, image.Height, visualContext,
                            "panorama=完整遊戲畫面；command_panel=左下指令面板；minimap=右下小地圖", null, null);
                        var state = ToState(sample.State, now);
                        var context = new SituationContext(state,
                            GameHistorySummarizer.Summarize(new GameHistory(), TimeSpan.FromSeconds(1), now),
                            null, PreviousPlan(now), visualContext.Events, now, lease.Observation,
                            new StrategyDirective("benchmark", sample.TargetAge), scope);
                        var clock = Stopwatch.StartNew();
                        var result = await planner.PlanAsync(context, cancellationToken);
                        clock.Stop();
                        lease.Complete(result.Success);
                        if (!warmed) { warmed = true; continue; }
                        var action = result.Plan?.VisualDecision?.Action.Kind;
                        var quality = result.Success &&
                            (sample.AllowedActions.Count == 0 || action is { } allowed && sample.AllowedActions.Contains(allowed)) &&
                            (action is null || !sample.ForbiddenActions.Contains(action.Value));
                        runs.Add(new(sample.Id, contract.Id, contract.Revision, scope, repetition,
                            clock.ElapsedMilliseconds, action, result.Success, quality, result.Error, planner.LastTelemetry));
                    }
                }
            }
        }

        var coveredTags = manifest.Snapshots.SelectMany(item => item.Tags)
            .Concat(manifest.Sequences.SelectMany(item => item.Tags)).ToHashSet(StringComparer.Ordinal);
        var missingCoverage = manifest.RequiredCoverageTags.Where(tag => !coveredTags.Contains(tag)).ToArray();
        var summaries = BuildSummaries(runs);
        var promotion = EvaluatePromotion(runs, summaries, missingCoverage.Length == 0);
        var report = new GamePlanContractBenchmarkReport(1, DateTimeOffset.UtcNow, manifestPath,
            VlmPipelinePresetCatalog.Legacy.Id, runs, summaries, missingCoverage, promotion);
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(report, JsonOptions));
        return report;
    }

    private static IReadOnlyList<GamePlanContractScopeSummary> BuildSummaries(IReadOnlyList<GamePlanContractRun> runs) => runs
        .GroupBy(run => (run.ContractId, run.Scope))
        .Select(group =>
        {
            var contract = GamePlanContractCatalog.Get(group.Key.ContractId);
            var budget = contract.CompletionBudgets[group.Key.Scope];
            var successful = group.Where(run => run.Success && run.Telemetry is not null).ToArray();
            var completions = successful.Where(run => run.Telemetry!.CompletionTokens is not null)
                .Select(run => (double)run.Telemetry!.CompletionTokens!.Value).ToArray();
            var predicted = successful.Where(run => run.Telemetry!.PredictedMilliseconds is not null)
                .Select(run => run.Telemetry!.PredictedMilliseconds!.Value).ToArray();
            var e2e = successful.Select(run => (double)run.EndToEndMilliseconds).ToArray();
            var medianCompletion = Median(completions);
            return new GamePlanContractScopeSummary(group.Key.ContractId, contract.Revision, group.Key.Scope, group.Count(), successful.Length,
                group.Count(run => run.QualityPassed), medianCompletion, Percentile(completions, .95),
                Median(predicted), Percentile(predicted, .95), Median(e2e), Percentile(e2e, .95),
                budget.TargetMedian, budget.PromotionCeiling, budget.HardCap,
                medianCompletion <= budget.TargetMedian, medianCompletion <= budget.PromotionCeiling,
                completions.Length > 0 && completions.All(tokens => tokens <= budget.HardCap));
        }).OrderBy(item => item.ContractId).ThenBy(item => item.Scope).ToArray();

    private static GamePlanContractPromotion EvaluatePromotion(IReadOnlyList<GamePlanContractRun> runs,
        IReadOnlyList<GamePlanContractScopeSummary> summaries, bool coverageComplete)
    {
        var results = new List<GamePlanContractScopePromotion>();
        foreach (var scope in Enum.GetValues<PlanUpdateScope>())
        {
            var baseline = summaries.Single(item => item.ContractId == GamePlanContractCatalog.Legacy.Id && item.Scope == scope);
            var candidate = summaries.Single(item => item.ContractId == GamePlanContractCatalog.CompactV2.Id && item.Scope == scope);
            var paired = runs.Where(run => run.Scope == scope)
                .GroupBy(run => (run.CaseId, run.Repetition))
                .Where(group => group.Any(run => run.ContractId == GamePlanContractCatalog.Legacy.Id) &&
                                group.Any(run => run.ContractId == GamePlanContractCatalog.CompactV2.Id)).ToArray();
            var actionParity = paired.Count() > 0 && paired.All(group =>
                group.Single(run => run.ContractId == GamePlanContractCatalog.Legacy.Id).Action ==
                group.Single(run => run.ContractId == GamePlanContractCatalog.CompactV2.Id).Action);
            var quality = candidate.SuccessfulRuns == candidate.TotalRuns && candidate.QualityPassedRuns == candidate.TotalRuns;
            results.Add(new(scope, quality, actionParity, candidate.PromotionCeilingPassed, candidate.HardCapPassed,
                candidate.MedianCompletionTokens < baseline.MedianCompletionTokens,
                candidate.MedianPredictedMilliseconds < baseline.MedianPredictedMilliseconds,
                candidate.MedianEndToEndMilliseconds < baseline.MedianEndToEndMilliseconds,
                coverageComplete && quality && actionParity && candidate.PromotionCeilingPassed && candidate.HardCapPassed &&
                candidate.MedianCompletionTokens < baseline.MedianCompletionTokens &&
                candidate.MedianPredictedMilliseconds < baseline.MedianPredictedMilliseconds &&
                candidate.MedianEndToEndMilliseconds < baseline.MedianEndToEndMilliseconds));
        }
        return new(coverageComplete, results, coverageComplete && results.All(item => item.Eligible));
    }

    private static GamePlan PreviousPlan(DateTimeOffset now) => new(
        "contract-benchmark-previous", now.AddSeconds(-1), now.AddSeconds(59), "benchmark", "既有小目標", "benchmark", .9,
        VisualDecision: new VisualPlayerDecision("benchmark", "既有小目標", "benchmark", new GameAction(GameActionKind.Wait),
            "重新觀察", 1000, .9),
        MajorDecision: new DecisionNode("major-existing", DecisionLevel.Major, "既有大目標", "benchmark", "benchmark", "benchmark", "benchmark"),
        MediumDecision: new DecisionNode("medium-existing", DecisionLevel.Medium, "既有中目標", "benchmark", "benchmark", "benchmark", "benchmark"),
        MinorDecision: new DecisionNode("minor-existing", DecisionLevel.Minor, "既有小目標", "benchmark", "benchmark", "benchmark", "benchmark"));

    private static GameState ToState(VlmBenchmarkState state, DateTimeOffset at) => new()
    {
        Age = state.Age,
        Food = Observed(state.Food, at), Wood = Observed(state.Wood, at), Gold = Observed(state.Gold, at), Stone = Observed(state.Stone, at),
        Population = Observed(state.Population, at), PopulationCap = Observed(state.PopulationCap, at),
    };

    private static ObservedValue<int> Observed(int? value, DateTimeOffset at) => value is { } actual
        ? new(actual, .99, at, ObservationStatus.Confirmed) : ObservedValue<int>.Unavailable(at);
    private static double Median(IEnumerable<double> values) => Percentile(values, .5);
    private static double Percentile(IEnumerable<double> source, double percentile)
    {
        var values = source.Order().ToArray();
        if (values.Length == 0) return 0;
        return values[Math.Clamp((int)Math.Ceiling(values.Length * percentile) - 1, 0, values.Length - 1)];
    }
    private static string Resolve(string root, string path) => Path.IsPathRooted(path) ? path : Path.GetFullPath(path, root);
}

public sealed record GamePlanContractRun(string CaseId, string ContractId, int ContractRevision,
    PlanUpdateScope Scope, int Repetition, long EndToEndMilliseconds, GameActionKind? Action,
    bool Success, bool QualityPassed, string? Error, PlannerRequestTelemetry? Telemetry);

public sealed record GamePlanContractScopeSummary(string ContractId, int ContractRevision, PlanUpdateScope Scope,
    int TotalRuns, int SuccessfulRuns, int QualityPassedRuns, double MedianCompletionTokens, double P95CompletionTokens,
    double MedianPredictedMilliseconds, double P95PredictedMilliseconds,
    double MedianEndToEndMilliseconds, double P95EndToEndMilliseconds,
    int TargetMedianTokens, int PromotionCeilingTokens, int HardCapTokens,
    bool TargetPassed, bool PromotionCeilingPassed, bool HardCapPassed);

public sealed record GamePlanContractScopePromotion(PlanUpdateScope Scope, bool QualityPassed, bool ActionParityPassed,
    bool PromotionCeilingPassed, bool HardCapPassed, bool CompletionImproved, bool DecodeImproved,
    bool EndToEndImproved, bool Eligible);

public sealed record GamePlanContractPromotion(bool CoverageComplete,
    IReadOnlyList<GamePlanContractScopePromotion> Scopes, bool Eligible);

public sealed record GamePlanContractBenchmarkReport(int SchemaVersion, DateTimeOffset GeneratedAt,
    string ManifestPath, string FixedImagePresetId, IReadOnlyList<GamePlanContractRun> Runs,
    IReadOnlyList<GamePlanContractScopeSummary> Summaries, IReadOnlyList<string> MissingCoverageTags,
    GamePlanContractPromotion Promotion);
