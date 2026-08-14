namespace EMF.Intelligence.Capabilities;

public sealed class TextKeywordExtractionRequest
{
    public TextKeywordExtractionRequest(
        string text,
        int maximumKeywords,
        int minimumKeywordLength = 3,
        IReadOnlyCollection<string>? excludedTerms =
            null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            text);

        if (maximumKeywords <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumKeywords));
        }

        if (minimumKeywordLength <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumKeywordLength));
        }

        var exclusions =
            excludedTerms?.ToArray() ??
            Array.Empty<string>();

        if (exclusions.Any(
                string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Excluded terms cannot be empty.",
                nameof(excludedTerms));
        }

        Text = text;
        MaximumKeywords = maximumKeywords;
        MinimumKeywordLength =
            minimumKeywordLength;
        ExcludedTerms =
            exclusions
                .Select(term => term.Trim())
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    public string Text { get; }

    public int MaximumKeywords { get; }

    public int MinimumKeywordLength { get; }

    public IReadOnlyList<string> ExcludedTerms
    { get; }
}
