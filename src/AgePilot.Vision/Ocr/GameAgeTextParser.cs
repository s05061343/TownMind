using AgePilot.Core;

namespace AgePilot.Vision.Ocr;

public static class GameAgeTextParser
{
    public static GameAge? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text.Contains("黑暗", StringComparison.Ordinal)) return GameAge.Dark;
        if (text.Contains("封建", StringComparison.Ordinal)) return GameAge.Feudal;
        if (text.Contains("城堡", StringComparison.Ordinal)) return GameAge.Castle;
        if (text.Contains("帝王", StringComparison.Ordinal)) return GameAge.Imperial;
        return null;
    }
}
