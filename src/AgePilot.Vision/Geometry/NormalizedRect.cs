namespace AgePilot.Vision.Geometry;

public readonly record struct NormalizedRect(double X, double Y, double Width, double Height)
{
    public PixelRect ToPixels(int frameWidth, int frameHeight)
    {
        if (frameWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameWidth));
        }

        if (frameHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameHeight));
        }

        Validate();

        var left = Math.Clamp((int)Math.Floor(X * frameWidth), 0, frameWidth);
        var top = Math.Clamp((int)Math.Floor(Y * frameHeight), 0, frameHeight);
        var right = Math.Clamp((int)Math.Ceiling((X + Width) * frameWidth), left, frameWidth);
        var bottom = Math.Clamp((int)Math.Ceiling((Y + Height) * frameHeight), top, frameHeight);

        return new PixelRect(left, top, right - left, bottom - top);
    }

    public void Validate()
    {
        if (!IsUnitValue(X) || !IsUnitValue(Y) || Width <= 0 || Height <= 0 ||
            X + Width > 1 || Y + Height > 1)
        {
            throw new InvalidDataException(
                $"Normalized rectangle must be inside [0,1]: ({X}, {Y}, {Width}, {Height}).");
        }
    }

    private static bool IsUnitValue(double value) => value is >= 0 and <= 1;
}
