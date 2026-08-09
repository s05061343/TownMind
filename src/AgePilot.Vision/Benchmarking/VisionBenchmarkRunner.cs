using System.Diagnostics;
using System.Text.Json;
using AgePilot.Core;
using AgePilot.Vision.Ocr;
using AgePilot.Vision.Profiles;
using AgePilot.Core.History;
using AgePilot.Core.Rules;
using AgePilot.Vision.Observations;

namespace AgePilot.Vision.Benchmarking;

public static class VisionBenchmarkRunner
{
    public static VisionBenchmarkReport Run(string manifestPath)
    {
        manifestPath = Path.GetFullPath(manifestPath);
        var manifestDirectory = Path.GetDirectoryName(manifestPath)
            ?? throw new InvalidDataException("Manifest directory is unavailable.");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        using var engine = new PaddleNumericOcrEngine();
        var analyzer = new HudOcrAnalyzer(engine);
        var samples = new List<VisionSampleResult>();

        foreach (var sample in document.RootElement.GetProperty("samples").EnumerateArray())
        {
            var id = sample.GetProperty("id").GetString() ?? throw new InvalidDataException("Sample id is required.");
            var imagePath = Path.GetFullPath(sample.GetProperty("image").GetString()!, manifestDirectory);
            var profilePath = Path.GetFullPath(sample.GetProperty("profile").GetString()!, manifestDirectory);
            var truth = sample.GetProperty("groundTruth");
            var stopwatch = Stopwatch.StartNew();
            var result = analyzer.AnalyzeJpeg(imagePath, HudProfileLoader.Load(profilePath));
            stopwatch.Stop();

            var fields = new List<VisionFieldResult>
            {
                Compare("wood", truth.GetProperty("wood").GetInt32(), result.Fields[HudField.Wood]),
                Compare("food", truth.GetProperty("food").GetInt32(), result.Fields[HudField.Food]),
                Compare("gold", truth.GetProperty("gold").GetInt32(), result.Fields[HudField.Gold]),
                Compare("stone", truth.GetProperty("stone").GetInt32(), result.Fields[HudField.Stone]),
                Compare("population", truth.GetProperty("population").GetInt32(), result.Population?.Current, result.Fields[HudField.Population]),
                Compare("populationCap", truth.GetProperty("populationCap").GetInt32(), result.Population?.Cap, result.Fields[HudField.Population]),
            };
            if (truth.TryGetProperty("age", out var expectedAge))
            {
                var expected = Enum.Parse<GameAge>(expectedAge.GetString()!);
                fields.Add(new VisionFieldResult("age", expected.ToString(), result.Age?.ToString(),
                    result.AgeObservation?.Confidence ?? 0, expected == result.Age));
            }

            var expectedPaused = sample.TryGetProperty("paused", out var paused) && paused.GetBoolean();
            var pauseCorrect = expectedPaused == result.IsPauseMenuVisible;
            var expectedRecommendations = sample.TryGetProperty("expectedRecommendations", out var expectedRules)
                ? expectedRules.EnumerateArray().Select(item => item.GetString()!).Order().ToArray()
                : [];
            var actualRecommendations = Array.Empty<string>();
            if (!result.IsPauseMenuVisible)
            {
                var state = new TemporalGameStateEstimator().Update(result, DateTimeOffset.UtcNow);
                actualRecommendations = CreateCoach().Evaluate(state, new GameHistory()).Select(item => item.Id).Order().ToArray();
            }
            var recommendationsExact = expectedRecommendations.SequenceEqual(actualRecommendations);
            samples.Add(new VisionSampleResult(id, stopwatch.Elapsed.TotalMilliseconds, fields, expectedPaused,
                result.IsPauseMenuVisible, pauseCorrect, expectedRecommendations, actualRecommendations,
                recommendationsExact, fields.All(field => field.Exact) && pauseCorrect,
                fields.All(field => field.Exact) && pauseCorrect && recommendationsExact));
        }

        var allFields = samples.SelectMany(sample => sample.Fields).ToArray();
        var highConfidence = allFields.Where(field => field.Confidence >= 0.90).ToArray();
        var sortedLatencies = samples.Select(sample => sample.LatencyMilliseconds).Order().ToArray();
        var actualRecommendationCount = samples.Sum(sample => sample.ActualRecommendations.Count);
        var falseRecommendationCount = samples.Sum(sample => sample.ActualRecommendations.Count(actual => !sample.ExpectedRecommendations.Contains(actual)));
        return new VisionBenchmarkReport(
            1,
            DateTimeOffset.UtcNow,
            Path.GetFileName(manifestPath),
            samples,
            Ratio(allFields.Count(field => field.Exact), allFields.Length),
            Ratio(samples.Count(sample => sample.FrameExact), samples.Count),
            Ratio(highConfidence.Count(field => !field.Exact), highConfidence.Length),
            Ratio(allFields.Count(field => field.Actual is null), allFields.Length),
            Ratio(falseRecommendationCount, actualRecommendationCount),
            Ratio(samples.Count(sample => sample.RecommendationsExact), samples.Count),
            sortedLatencies.Length == 0 ? 0 : sortedLatencies.Average(),
            Percentile(sortedLatencies, 0.95));
    }

    public static void WriteJson(VisionBenchmarkReport report, string outputPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static VisionFieldResult Compare(string field, int expected, OcrResult observation) =>
        Compare(field, expected, observation.Value, observation);

    private static VisionFieldResult Compare(string field, int expected, int? actual, OcrResult observation) =>
        new(field, expected.ToString(), actual?.ToString(), observation.Confidence, actual == expected);

    private static double Ratio(int numerator, int denominator) => denominator == 0 ? 0 : (double)numerator / denominator;

    private static double Percentile(double[] sortedValues, double percentile)
    {
        if (sortedValues.Length == 0) return 0;
        var index = (int)Math.Ceiling(percentile * sortedValues.Length) - 1;
        return sortedValues[Math.Clamp(index, 0, sortedValues.Length - 1)];
    }

    private static CoachEngine CreateCoach() => new([
        new PopulationCriticalRule(), new PopulationLowRule(), new WoodOverflowRule(),
        new GoldLowForCastleRule(), new CastleReadyRule(), new ImperialReadyRule(), new ResourceOverflowRule(),
    ]);
}

public sealed record VisionFieldResult(string Field, string Expected, string? Actual, double Confidence, bool Exact);

public sealed record VisionSampleResult(
    string Id,
    double LatencyMilliseconds,
    IReadOnlyList<VisionFieldResult> Fields,
    bool ExpectedPaused,
    bool ActualPaused,
    bool PauseExact,
    IReadOnlyList<string> ExpectedRecommendations,
    IReadOnlyList<string> ActualRecommendations,
    bool RecommendationsExact,
    bool FrameExact,
    bool PipelineExact);

public sealed record VisionBenchmarkReport(
    int SchemaVersion,
    DateTimeOffset GeneratedAt,
    string Manifest,
    IReadOnlyList<VisionSampleResult> Samples,
    double FieldExactAccuracy,
    double FrameExactAccuracy,
    double HighConfidenceErrorRate,
    double UnavailableRate,
    double FalseRecommendationRate,
    double RecommendationExactRate,
    double AverageLatencyMilliseconds,
    double P95LatencyMilliseconds);
