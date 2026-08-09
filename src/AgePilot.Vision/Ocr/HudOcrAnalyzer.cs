using AgePilot.Vision.Images;
using AgePilot.Vision.Profiles;

namespace AgePilot.Vision.Ocr;

public sealed class HudOcrAnalyzer(PaddleNumericOcrEngine engine)
{
    public HudOcrResult AnalyzeJpeg(string imagePath, HudProfile profile)
    {
        var size = BmpInfoReader.ReadJpegSize(imagePath);
        var orderedFields = Enum.GetValues<HudField>();
        var regions = orderedFields
            .Select(field => profile.Regions[field].ToPixels(size.Width, size.Height))
            .ToList();
        if (profile.AgeRegion is { } ageRegion) regions.Add(ageRegion.ToPixels(size.Width, size.Height));
        if (profile.PauseMenuRegion is { } pauseMenuRegion) regions.Add(pauseMenuRegion.ToPixels(size.Width, size.Height));
        var observations = engine.RecognizeFile(imagePath, regions);
        return CreateResult(orderedFields, observations, profile.AgeRegion is not null, profile.PauseMenuRegion is not null);
    }

    public HudOcrResult AnalyzeFrame(
        ReadOnlyMemory<byte> bgraPixels,
        int width,
        int height,
        HudProfile profile)
    {
        var orderedFields = Enum.GetValues<HudField>();
        var regions = orderedFields
            .Select(field => profile.Regions[field].ToPixels(width, height))
            .ToList();
        if (profile.AgeRegion is { } ageRegion) regions.Add(ageRegion.ToPixels(width, height));
        if (profile.PauseMenuRegion is { } pauseMenuRegion) regions.Add(pauseMenuRegion.ToPixels(width, height));
        var observations = engine.RecognizeFrame(bgraPixels, width, height, regions);
        return CreateResult(orderedFields, observations, profile.AgeRegion is not null, profile.PauseMenuRegion is not null);
    }

    private static HudOcrResult CreateResult(
        IReadOnlyList<HudField> orderedFields,
        IReadOnlyList<OcrResult> observations,
        bool hasAgeRegion,
        bool hasPauseMenuRegion)
    {
        var fields = orderedFields
            .Select((field, index) => (field, observations[index]))
            .ToDictionary(pair => pair.field, pair => pair.Item2);
        var populationRaw = fields[HudField.Population].RawText;
        var nextIndex = orderedFields.Count;
        var ageObservation = hasAgeRegion ? observations[nextIndex++] : null;
        var pauseMenuObservation = hasPauseMenuRegion ? observations[nextIndex] : null;
        return new HudOcrResult(
            fields,
            PopulationTextParser.Parse(populationRaw),
            GameAgeTextParser.Parse(ageObservation?.RawText),
            ageObservation,
            PauseMenuTextParser.IsVisible(pauseMenuObservation?.RawText),
            pauseMenuObservation);
    }
}

public sealed record HudOcrResult(
    IReadOnlyDictionary<HudField, OcrResult> Fields,
    PopulationValue? Population,
    AgePilot.Core.GameAge? Age = null,
    OcrResult? AgeObservation = null,
    bool IsPauseMenuVisible = false,
    OcrResult? PauseMenuObservation = null);
