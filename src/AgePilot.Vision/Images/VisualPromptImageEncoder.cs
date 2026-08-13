using OpenCvSharp;
using AgePilot.Core.Planning;
using AgePilot.Vision.Geometry;

namespace AgePilot.Vision.Images;

public static class VisualPromptImageEncoder
{
    // ADR 0014 曾在此疊加座標網格，用於輔助模型估算 normalized 座標。
    // ADR 0015 之後模型不再輸出任何座標，該輔助失去用途，已移除。
    public static IReadOnlyList<VisualImage> Encode(
        ReadOnlySpan<byte> bgra, int width, int height,
        NormalizedRect commandPanel, NormalizedRect minimap)
    {
        using var source = Mat.FromPixelData(height, width, MatType.CV_8UC4, bgra.ToArray());
        using var panorama = new Mat();
        Cv2.Resize(source, panorama, new Size(1536, 864), 0, 0, InterpolationFlags.Area);
        return
        [
            Image("panorama", panorama),
            Crop("command_panel", source, commandPanel.ToPixels(width, height)),
            Crop("minimap", source, minimap.ToPixels(width, height)),
        ];
    }

    private static VisualImage Crop(string name, Mat source, PixelRect region)
    {
        using var crop = new Mat(source, new Rect(region.X, region.Y, region.Width, region.Height));
        return Image(name, crop);
    }

    private static VisualImage Image(string name, Mat image)
    {
        Cv2.ImEncode(".jpg", image, out var bytes, [new ImageEncodingParam(ImwriteFlags.JpegQuality, 85)]);
        return new(name, "image/jpeg", bytes);
    }
}
