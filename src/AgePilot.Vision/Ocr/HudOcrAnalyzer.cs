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
            .ToArray();
        var observations = engine.RecognizeFile(imagePath, regions);
        return CreateResult(orderedFields, observations);
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
            .ToArray();
        var observations = engine.RecognizeFrame(bgraPixels, width, height, regions);
        return CreateResult(orderedFields, observations);
    }

    private static HudOcrResult CreateResult(
        IReadOnlyList<HudField> orderedFields,
        IReadOnlyList<OcrResult> observations)
    {
        var fields = orderedFields
            .Select((field, index) => (field, observations[index]))
            .ToDictionary(pair => pair.field, pair => pair.Item2);
        var populationRaw = fields[HudField.Population].RawText;
        return new HudOcrResult(fields, PopulationTextParser.Parse(populationRaw));
    }
}

public sealed record HudOcrResult(
    IReadOnlyDictionary<HudField, OcrResult> Fields,
    PopulationValue? Population);
