namespace AgePilot.Vision.Images;

public readonly record struct ImageSize(int Width, int Height);

public static class BmpInfoReader
{
    public static ImageSize ReadJpegSize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        if (reader.ReadByte() != 0xFF || reader.ReadByte() != 0xD8)
        {
            throw new InvalidDataException("File is not a JPEG image.");
        }

        while (stream.Position < stream.Length)
        {
            if (reader.ReadByte() != 0xFF)
            {
                continue;
            }

            byte marker;
            do
            {
                marker = reader.ReadByte();
            }
            while (marker == 0xFF);

            if (marker is 0xD8 or 0xD9)
            {
                continue;
            }

            var length = ReadBigEndianUInt16(reader);
            if (length < 2)
            {
                throw new InvalidDataException("JPEG segment length is invalid.");
            }

            if (IsStartOfFrame(marker))
            {
                _ = reader.ReadByte();
                var height = ReadBigEndianUInt16(reader);
                var width = ReadBigEndianUInt16(reader);
                return new ImageSize(width, height);
            }

            stream.Seek(length - 2, SeekOrigin.Current);
        }

        throw new InvalidDataException("JPEG dimensions were not found.");
    }

    private static ushort ReadBigEndianUInt16(BinaryReader reader)
    {
        var high = reader.ReadByte();
        var low = reader.ReadByte();
        return (ushort)((high << 8) | low);
    }

    private static bool IsStartOfFrame(byte marker) =>
        marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or
            0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;
}
