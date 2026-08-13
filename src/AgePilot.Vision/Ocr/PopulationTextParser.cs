namespace AgePilot.Vision.Ocr;

public readonly record struct PopulationValue(int Current, int Cap);

public enum PopulationParseKind
{
    LiteralSeparator = 4,
    AlternateSeparator = 3,
    SlashRecognizedAsOne = 2,
    MissingSeparator = 1,
}

public readonly record struct PopulationParseResult(PopulationValue Value, PopulationParseKind Kind);

public static class PopulationTextParser
{
    private const int MaximumSupportedPopulation = 500;

    public static PopulationValue? Parse(string? text) => ParseDetailed(text)?.Value;

    public static PopulationParseResult? ParseDetailed(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var compact = new string(text.Where(character => !char.IsWhiteSpace(character)).ToArray());

        char[] literalSeparators = ['/', '／'];
        if (compact.IndexOfAny(literalSeparators) >= 0)
            return ParseUsingSeparators(compact, literalSeparators, PopulationParseKind.LiteralSeparator);

        char[] alternateSeparators = ['|', 'I', 'l', '\\'];
        if (compact.IndexOfAny(alternateSeparators) >= 0)
            return ParseUsingSeparators(compact, alternateSeparators, PopulationParseKind.AlternateSeparator);

        var digits = new string(compact.Where(char.IsAsciiDigit).ToArray());
        var slashAsOne = ParseAtPositions(
            digits,
            Enumerable.Range(1, Math.Max(0, digits.Length - 2)).Where(index => digits[index] == '1'),
            removeSeparatorCharacter: true,
            PopulationParseKind.SlashRecognizedAsOne);
        if (slashAsOne is not null) return slashAsOne;

        return ParseAtPositions(
            digits,
            Enumerable.Range(1, Math.Max(0, digits.Length - 1)),
            removeSeparatorCharacter: false,
            PopulationParseKind.MissingSeparator);
    }

    private static PopulationParseResult? ParseUsingSeparators(
        string text,
        IReadOnlyCollection<char> separators,
        PopulationParseKind kind)
    {
        var separatorIndexes = text
            .Select((character, index) => (character, index))
            .Where(item => separators.Contains(item.character))
            .Select(item => item.index)
            .ToArray();
        return ParseAtPositions(text, separatorIndexes, removeSeparatorCharacter: true, kind);
    }

    private static PopulationParseResult? ParseAtPositions(
        string text,
        IEnumerable<int> separatorIndexes,
        bool removeSeparatorCharacter,
        PopulationParseKind kind)
    {
        var candidates = separatorIndexes
            .Select(index => CreateCandidate(text, index, removeSeparatorCharacter))
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .Distinct()
            .ToArray();

        return candidates is [var only] ? new PopulationParseResult(only, kind) : null;
    }

    private static PopulationValue? CreateCandidate(string text, int separatorIndex, bool removeSeparatorCharacter)
    {
        var rightStart = separatorIndex + (removeSeparatorCharacter ? 1 : 0);
        if (separatorIndex <= 0 || rightStart >= text.Length) return null;

        var current = text[..separatorIndex];
        var cap = text[rightStart..];
        if (!current.All(char.IsAsciiDigit) || !cap.All(char.IsAsciiDigit) ||
            HasLeadingZero(current) || HasLeadingZero(cap) ||
            !int.TryParse(current, out var currentValue) || !int.TryParse(cap, out var capValue) ||
            currentValue < 0 || currentValue > MaximumSupportedPopulation ||
            capValue <= 0 || capValue > MaximumSupportedPopulation || currentValue > capValue)
        {
            return null;
        }

        return new PopulationValue(currentValue, capValue);
    }

    private static bool HasLeadingZero(string value) => value.Length > 1 && value[0] == '0';
}
