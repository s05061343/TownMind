namespace AgePilot.Vision.Images;

public static class BgraBitmapWriter
{
    public static void Write(string path, int width, int height, ReadOnlySpan<byte> bgraPixels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var expectedLength = checked(width * height * 4);
        if (width <= 0 || height <= 0 || bgraPixels.Length != expectedLength)
        {
            throw new ArgumentException("Pixel buffer dimensions are invalid.", nameof(bgraPixels));
        }

        const int fileHeaderSize = 14;
        const int infoHeaderSize = 40;
        var pixelOffset = fileHeaderSize + infoHeaderSize;
        var fileSize = checked(pixelOffset + expectedLength);

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(fileSize);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(pixelOffset);

        writer.Write(infoHeaderSize);
        writer.Write(width);
        writer.Write(-height);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0);
        writer.Write(expectedLength);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        writer.Write(bgraPixels);
    }
}
