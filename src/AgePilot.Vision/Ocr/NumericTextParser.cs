namespace AgePilot.Vision.Ocr;

public static class NumericTextParser
{
    public static int? ParseNonNegativeInteger(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        Span<char> digits = stackalloc char[text.Length];
        var count = 0;

        foreach (var character in text)
        {
            if (char.IsAsciiDigit(character))
            {
                digits[count++] = character;
            }
        }

        return count > 0 && int.TryParse(digits[..count], out var value)
            ? value
            : null;
    }
}
