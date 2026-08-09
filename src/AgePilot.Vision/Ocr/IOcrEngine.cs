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

public sealed record OcrResult(string RawText, int? Value, double Confidence);
