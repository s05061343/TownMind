using AgePilot.Vision.Geometry;
using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models.Local;

namespace AgePilot.Vision.Ocr;

public sealed class PaddleNumericOcrEngine : IOcrEngine, IDisposable
{
    private readonly PaddleOcrRecognizer _recognizer;

    public PaddleNumericOcrEngine()
    {
        _recognizer = new PaddleOcrRecognizer(
            LocalRecognizationModel.EnglishV5,
            PaddleDevice.OneDnn(cacheCapacity: 10, cpuMathThreadCount: 1));
    }

    public ValueTask<OcrResult> RecognizeNumberAsync(
        ReadOnlyMemory<byte> bgraPixels,
        int frameWidth,
        int frameHeight,
        PixelRect region,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var expectedLength = checked(frameWidth * frameHeight * 4);
        if (bgraPixels.Length != expectedLength)
        {
            throw new ArgumentException("BGRA frame size does not match its dimensions.", nameof(bgraPixels));
        }

        return ValueTask.FromResult(
            RecognizeFrame(bgraPixels, frameWidth, frameHeight, [region])[0]);
    }

    public OcrResult RecognizeFile(string imagePath, PixelRect region)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);

        using var image = Cv2.ImRead(imagePath, ImreadModes.Color);
        if (image.Empty())
        {
            throw new InvalidDataException($"Unable to decode image: {imagePath}");
        }

        return Recognize(image, region);
    }

    public IReadOnlyList<OcrResult> RecognizeFile(
        string imagePath,
        IReadOnlyList<PixelRect> regions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);

        using var image = Cv2.ImRead(imagePath, ImreadModes.Color);
        if (image.Empty())
        {
            throw new InvalidDataException($"Unable to decode image: {imagePath}");
        }

        return Recognize(image, regions);
    }

    public IReadOnlyList<OcrResult> RecognizeFrame(
        ReadOnlyMemory<byte> bgraPixels,
        int frameWidth,
        int frameHeight,
        IReadOnlyList<PixelRect> regions)
    {
        var expectedLength = checked(frameWidth * frameHeight * 4);
        if (bgraPixels.Length != expectedLength)
        {
            throw new ArgumentException("BGRA frame size does not match its dimensions.", nameof(bgraPixels));
        }

        using var frame = Mat.FromPixelData(
            frameHeight,
            frameWidth,
            MatType.CV_8UC4,
            bgraPixels.ToArray());
        using var bgr = new Mat();
        Cv2.CvtColor(frame, bgr, ColorConversionCodes.BGRA2BGR);
        return Recognize(bgr, regions);
    }

    private IReadOnlyList<OcrResult> Recognize(Mat image, IReadOnlyList<PixelRect> regions)
    {
        var inputs = new Mat[regions.Count];
        try
        {
            for (var index = 0; index < regions.Count; index++)
            {
                var region = regions[index];
                ValidateRegion(image, region);

                using var cropped = new Mat(
                    image,
                    new Rect(region.X, region.Y, region.Width, region.Height));
                inputs[index] = new Mat();
                Cv2.Resize(cropped, inputs[index], new Size(), 4, 4, InterpolationFlags.Cubic);
            }

            var results = _recognizer.Run(inputs, batchSize: inputs.Length);
            return results
                .Select(result => new OcrResult(
                    result.Text.Trim(),
                    NumericTextParser.ParseNonNegativeInteger(result.Text),
                    Math.Clamp(result.Score, 0, 1)))
                .ToArray();
        }
        finally
        {
            foreach (var input in inputs)
            {
                input?.Dispose();
            }
        }
    }

    public void Dispose() => _recognizer.Dispose();

    private OcrResult Recognize(Mat frame, PixelRect region)
    {
        ValidateRegion(frame, region);

        using var cropped = new Mat(
            frame,
            new Rect(region.X, region.Y, region.Width, region.Height));
        using var enlarged = new Mat();
        Cv2.Resize(cropped, enlarged, new Size(), 4, 4, InterpolationFlags.Cubic);

        var result = _recognizer.Run(enlarged);
        return new OcrResult(
            result.Text.Trim(),
            NumericTextParser.ParseNonNegativeInteger(result.Text),
            Math.Clamp(result.Score, 0, 1));
    }

    private static void ValidateRegion(Mat frame, PixelRect region)
    {
        if (region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0 ||
            region.Right > frame.Width || region.Bottom > frame.Height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(region),
                $"ROI {region} is outside image {frame.Width}x{frame.Height}.");
        }
    }
}
