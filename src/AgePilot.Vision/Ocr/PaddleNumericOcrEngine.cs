using AgePilot.Vision.Geometry;
using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models.Local;
using System.Runtime.InteropServices;

namespace AgePilot.Vision.Ocr;

public enum OcrRecognitionModel { Chinese, English }

public sealed class PaddleNumericOcrEngine : IOcrEngine, IFrameOcrEngine, IPopulationOcrEngine, IDisposable
{
    private const double PreprocessScale = 2;
    private const double PopulationPreprocessScale = 3;
    private const double MinimumPopulationConfidence = 0.45;
    private readonly PaddleOcrRecognizer _recognizer;

    public PaddleNumericOcrEngine(OcrRecognitionModel model = OcrRecognitionModel.Chinese)
    {
        Environment.SetEnvironmentVariable("GLOG_minloglevel", "2");
        _recognizer = new PaddleOcrRecognizer(
            model == OcrRecognitionModel.English ? LocalRecognizationModel.EnglishV5 : LocalRecognizationModel.ChineseV5,
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

        var frameBytes = MemoryMarshal.TryGetArray(bgraPixels, out ArraySegment<byte> segment) &&
                         segment.Offset == 0 && segment.Count == bgraPixels.Length
            ? segment.Array!
            : bgraPixels.ToArray();
        using var frame = Mat.FromPixelData(
            frameHeight,
            frameWidth,
            MatType.CV_8UC4,
            frameBytes);
        return Recognize(frame, regions);
    }

    public OcrResult RefinePopulation(
        ReadOnlyMemory<byte> bgraPixels,
        int frameWidth,
        int frameHeight,
        PixelRect region,
        OcrResult baseline)
    {
        var expectedLength = checked(frameWidth * frameHeight * 4);
        if (bgraPixels.Length != expectedLength)
        {
            throw new ArgumentException("BGRA frame size does not match its dimensions.", nameof(bgraPixels));
        }

        var frameBytes = MemoryMarshal.TryGetArray(bgraPixels, out ArraySegment<byte> segment) &&
                         segment.Offset == 0 && segment.Count == bgraPixels.Length
            ? segment.Array!
            : bgraPixels.ToArray();
        using var frame = Mat.FromPixelData(frameHeight, frameWidth, MatType.CV_8UC4, frameBytes);
        ValidateRegion(frame, region);
        using var cropped = new Mat(frame, new Rect(region.X, region.Y, region.Width, region.Height));
        using var gray = new Mat();
        Cv2.CvtColor(cropped, gray, ColorConversionCodes.BGRA2GRAY);
        using var enlarged = new Mat();
        Cv2.Resize(gray, enlarged, new Size(), PopulationPreprocessScale, PopulationPreprocessScale, InterpolationFlags.Cubic);
        using var contrast = new Mat();
        Cv2.EqualizeHist(enlarged, contrast);
        using var binary = new Mat();
        Cv2.Threshold(contrast, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
        using var inverted = new Mat();
        Cv2.BitwiseNot(binary, inverted);

        var variants = new[] { enlarged, contrast, binary, inverted };
        var alternatives = _recognizer.Run(variants, batchSize: variants.Length)
            .Select(result => new OcrResult(
                result.Text.Trim(),
                NumericTextParser.ParseNonNegativeInteger(result.Text),
                Math.Clamp(result.Score, 0, 1)));

        return alternatives
            .Prepend(baseline)
            .Select(result => (Result: result, Parsed: PopulationTextParser.ParseDetailed(result.RawText)))
            .Where(item => item.Parsed is not null && item.Result.Confidence >= MinimumPopulationConfidence)
            .OrderByDescending(item => item.Parsed!.Value.Kind)
            .ThenByDescending(item => item.Result.Confidence)
            .Select(item => item.Result)
            .FirstOrDefault() ?? baseline;
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
                if (cropped.Channels() == 4)
                {
                    using var bgrCrop = new Mat();
                    Cv2.CvtColor(cropped, bgrCrop, ColorConversionCodes.BGRA2BGR);
                    Cv2.Resize(bgrCrop, inputs[index], new Size(), PreprocessScale, PreprocessScale, InterpolationFlags.Cubic);
                }
                else
                {
                    Cv2.Resize(cropped, inputs[index], new Size(), PreprocessScale, PreprocessScale, InterpolationFlags.Cubic);
                }
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
        Cv2.Resize(cropped, enlarged, new Size(), PreprocessScale, PreprocessScale, InterpolationFlags.Cubic);

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
