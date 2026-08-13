using AgePilot.Vision.Geometry;

namespace AgePilot.Vision.Ocr;

public interface IOcrEngine
{
    ValueTask<OcrResult> RecognizeNumberAsync(
        ReadOnlyMemory<byte> bgraPixels,
        int frameWidth,
        int frameHeight,
        PixelRect region,
        CancellationToken cancellationToken);
}

public interface IFrameOcrEngine
{
    IReadOnlyList<OcrResult> RecognizeFrame(
        ReadOnlyMemory<byte> bgraPixels,
        int frameWidth,
        int frameHeight,
        IReadOnlyList<PixelRect> regions);
}

public interface IPopulationOcrEngine
{
    OcrResult RefinePopulation(
        ReadOnlyMemory<byte> bgraPixels,
        int frameWidth,
        int frameHeight,
        PixelRect region,
        OcrResult baseline);
}

public sealed record OcrResult(string RawText, int? Value, double Confidence);
