using OpenCvSharp;

namespace AgePilot.Vision.Images;

public static class BgraImageLoader
{
    public static BgraImage Load(string path)
    {
        using var source = Cv2.ImRead(path, ImreadModes.Color);
        if (source.Empty()) throw new InvalidDataException($"Unable to decode image: {path}");
        using var bgra = new Mat();
        Cv2.CvtColor(source, bgra, ColorConversionCodes.BGR2BGRA);
        var bytes = new byte[checked(bgra.Width * bgra.Height * 4)];
        System.Runtime.InteropServices.Marshal.Copy(bgra.Data, bytes, 0, bytes.Length);
        return new BgraImage(bgra.Width, bgra.Height, bytes);
    }
}

public sealed record BgraImage(int Width, int Height, byte[] Pixels);
