using AgePilot.Core;
using AgePilot.Core.Observations;
using AgePilot.Vision.Ocr;
using AgePilot.Vision.Profiles;

namespace AgePilot.Vision.Observations;

public sealed class TemporalGameStateEstimator(
    int windowSize = 3,
    double minimumConfidence = 0.7,
    double temporalCandidateConfidence = 0.45)
{
    private readonly Dictionary<HudField, Queue<(int Value, double Confidence)>> _windows = new();

    public GameState Update(HudOcrResult result, DateTimeOffset observedAt)
    {
        var values = new Dictionary<HudField, ObservedValue<int>>();
        foreach (var field in Enum.GetValues<HudField>().Where(field => field != HudField.Population))
        {
            values[field] = UpdateField(field, result.Fields[field].Value, result.Fields[field].Confidence, observedAt);
        }

        var populationConfidence = result.Fields[HudField.Population].Confidence;
        var population = result.Population;
        var populationValue = UpdateField(HudField.Population, population?.Current, populationConfidence, observedAt);
        var capValue = population is null || populationConfidence < minimumConfidence
            ? ObservedValue<int>.Unavailable(observedAt)
            : new ObservedValue<int>(population.Value.Cap, populationConfidence, observedAt, ObservationStatus.Confirmed);

        return new GameState
        {
            Wood = values[HudField.Wood],
            Food = values[HudField.Food],
            Gold = values[HudField.Gold],
            Stone = values[HudField.Stone],
            Population = populationValue,
            PopulationCap = capValue,
        };
    }

    private ObservedValue<int> UpdateField(
        HudField field,
        int? rawValue,
        double confidence,
        DateTimeOffset observedAt)
    {
        if (rawValue is null || confidence < temporalCandidateConfidence)
        {
            return ObservedValue<int>.Unavailable(observedAt);
        }

        if (!_windows.TryGetValue(field, out var window))
        {
            window = new Queue<(int, double)>();
            _windows[field] = window;
        }

        window.Enqueue((rawValue.Value, confidence));
        while (window.Count > windowSize)
        {
            window.Dequeue();
        }

        var hasTemporalConfirmation = window.Count >= 2 &&
                                      window.Reverse().Take(2).All(item => item.Value == rawValue.Value);
        if (confidence < minimumConfidence && !hasTemporalConfirmation)
        {
            return ObservedValue<int>.Unavailable(observedAt);
        }

        var ordered = window.Select(item => item.Value).Order().ToArray();
        var median = ordered[ordered.Length / 2];
        var aggregateConfidence = window.Average(item => item.Confidence);
        return new ObservedValue<int>(median, aggregateConfidence, observedAt, ObservationStatus.Confirmed);
    }
}
