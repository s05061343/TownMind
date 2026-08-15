using System.Diagnostics;
using OpenCvSharp;
using AgePilot.Core.Configuration;
using AgePilot.Core.Planning;
using AgePilot.Vision.Geometry;

namespace AgePilot.Vision.Images;

public sealed record VisualImageBuildResult(
    IReadOnlyList<VisualImage> Images,
    long CropMilliseconds,
    long ResizeMilliseconds,
    long JpegEncodeMilliseconds);

public static class VisualPromptImageEncoder
{
    public static IReadOnlyList<VisualImage> Encode(
        ReadOnlySpan<byte> bgra, int width, int height,
        NormalizedRect commandPanel, NormalizedRect minimap)
    {
        var legacy = VlmPipelinePresetCatalog.Legacy;
        return Compose(bgra, width, height, legacy,
            new NormalizedRect(0, 0.045, 1, 0.615), commandPanel, minimap,
            includePanel: true, ["legacy-always"]).Images;
    }

    public static VisualImageBuildResult Compose(
        ReadOnlySpan<byte> bgra,
        int width,
        int height,
        VlmPipelinePreset preset,
        NormalizedRect battlefield,
        NormalizedRect commandPanel,
        NormalizedRect minimap,
        bool includePanel,
        IReadOnlyList<string> panelReasons)
    {
        using var source = Mat.FromPixelData(height, width, MatType.CV_8UC4, bgra.ToArray());
        var images = new List<VisualImage>(3);
        long cropMs = 0, resizeMs = 0, jpegMs = 0;

        if (preset.Composition == VlmImageComposition.LegacyThreeImages)
        {
            var result = ResizeAndEncode("panorama", source, 1536, 864, null);
            images.Add(result.Image);
            resizeMs += result.ResizeMs; jpegMs += result.JpegMs;
        }
        else
        {
            var result = CropResizeAndEncode("battlefield", source, battlefield.ToPixels(width, height),
                preset.BattlefieldMaxEdge, null);
            images.Add(result.Image);
            cropMs += result.CropMs; resizeMs += result.ResizeMs; jpegMs += result.JpegMs;
        }

        if (includePanel)
        {
            var result = CropResizeAndEncode("command_panel", source, commandPanel.ToPixels(width, height),
                maxEdge: null, string.Join('+', panelReasons));
            images.Add(result.Image);
            cropMs += result.CropMs; resizeMs += result.ResizeMs; jpegMs += result.JpegMs;
        }

        var minimapResult = CropResizeAndEncode("minimap", source, minimap.ToPixels(width, height), maxEdge: null, null);
        images.Add(minimapResult.Image);
        cropMs += minimapResult.CropMs; resizeMs += minimapResult.ResizeMs; jpegMs += minimapResult.JpegMs;

        return new(images, cropMs, resizeMs, jpegMs);
    }

    private static (VisualImage Image, long CropMs, long ResizeMs, long JpegMs) CropResizeAndEncode(
        string name, Mat source, PixelRect region, int? maxEdge, string? inclusionReason)
    {
        var cropClock = Stopwatch.StartNew();
        using var crop = new Mat(source, new Rect(region.X, region.Y, region.Width, region.Height));
        cropClock.Stop();
        var encoded = ResizeAndEncode(name, crop, maxEdge, inclusionReason);
        return (encoded.Image with { CropMilliseconds = cropClock.ElapsedMilliseconds },
            cropClock.ElapsedMilliseconds, encoded.ResizeMs, encoded.JpegMs);
    }

    private static (VisualImage Image, long ResizeMs, long JpegMs) ResizeAndEncode(
        string name, Mat source, int? maxEdge, string? inclusionReason)
    {
        if (maxEdge is null || Math.Max(source.Width, source.Height) <= maxEdge)
            return EncodeMat(name, source, 0, inclusionReason);

        var scale = maxEdge.Value / (double)Math.Max(source.Width, source.Height);
        var target = new Size(
            Math.Max(1, (int)Math.Round(source.Width * scale)),
            Math.Max(1, (int)Math.Round(source.Height * scale)));
        return ResizeAndEncode(name, source, target.Width, target.Height, inclusionReason);
    }

    private static (VisualImage Image, long ResizeMs, long JpegMs) ResizeAndEncode(
        string name, Mat source, int width, int height, string? inclusionReason)
    {
        var resizeClock = Stopwatch.StartNew();
        using var resized = new Mat();
        Cv2.Resize(source, resized, new Size(width, height), 0, 0, InterpolationFlags.Area);
        resizeClock.Stop();
        return EncodeMat(name, resized, resizeClock.ElapsedMilliseconds, inclusionReason);
    }

    private static (VisualImage Image, long ResizeMs, long JpegMs) EncodeMat(
        string name, Mat image, long resizeMs, string? inclusionReason)
    {
        var jpegClock = Stopwatch.StartNew();
        Cv2.ImEncode(".jpg", image, out var bytes, [new ImageEncodingParam(ImwriteFlags.JpegQuality, 85)]);
        jpegClock.Stop();
        return (new VisualImage(name, "image/jpeg", bytes, image.Width, image.Height,
            ResizeMilliseconds: resizeMs, JpegEncodeMilliseconds: jpegClock.ElapsedMilliseconds,
            InclusionReason: inclusionReason), resizeMs, jpegClock.ElapsedMilliseconds);
    }
}
