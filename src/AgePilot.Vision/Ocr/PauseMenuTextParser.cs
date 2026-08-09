namespace AgePilot.Vision.Ocr;

public static class PauseMenuTextParser
{
    public static bool IsVisible(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return false;
        var normalized = string.Concat(rawText.Where(character => !char.IsWhiteSpace(character)));
        return normalized.Contains("主選單", StringComparison.Ordinal) ||
               normalized.Contains("主菜单", StringComparison.Ordinal);
    }
}
