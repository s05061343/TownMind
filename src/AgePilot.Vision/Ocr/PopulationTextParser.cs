namespace AgePilot.Vision.Ocr;

public readonly record struct PopulationValue(int Current, int Cap);

public static class PopulationTextParser
{
    public static PopulationValue? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = new string(text
            .Where(character => char.IsAsciiDigit(character) || character == '/')
            .ToArray());

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts is [var current, var cap] &&
               int.TryParse(current, out var currentValue) &&
               int.TryParse(cap, out var capValue) &&
               currentValue >= 0 && capValue > 0 && currentValue <= capValue
            ? new PopulationValue(currentValue, capValue)
            : null;
    }
}
