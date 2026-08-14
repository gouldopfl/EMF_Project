using EMF.Intelligence.Capabilities;

namespace EMF.Laboratory;

public sealed class TextInsightLaboratoryOptions
{
    public TextInsightLaboratoryOptions(
        int maximumSegmentCharacters = 4_000,
        int overlapCharacters = 200,
        int maximumSummaryCharacters = 1_000,
        int maximumKeywords = 20,
        int minimumKeywordLength = 4,
        IReadOnlyCollection<string>? excludedTerms =
            null)
    {
        var validated =
            new LongTextInsightRequest(
                "validation text",
                maximumSegmentCharacters,
                overlapCharacters,
                maximumSummaryCharacters,
                maximumKeywords,
                minimumKeywordLength,
                excludedTerms);

        MaximumSegmentCharacters =
            validated.MaximumSegmentCharacters;
        OverlapCharacters =
            validated.OverlapCharacters;
        MaximumSummaryCharacters =
            validated.MaximumSummaryCharacters;
        MaximumKeywords =
            validated.MaximumKeywords;
        MinimumKeywordLength =
            validated.MinimumKeywordLength;
        ExcludedTerms =
            validated.ExcludedTerms.ToArray();
    }

    public int MaximumSegmentCharacters { get; }

    public int OverlapCharacters { get; }

    public int MaximumSummaryCharacters { get; }

    public int MaximumKeywords { get; }

    public int MinimumKeywordLength { get; }

    public IReadOnlyList<string> ExcludedTerms
    { get; }
}
