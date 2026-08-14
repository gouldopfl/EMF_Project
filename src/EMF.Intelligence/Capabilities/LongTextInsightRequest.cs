namespace EMF.Intelligence.Capabilities;

public sealed class LongTextInsightRequest
{
    public LongTextInsightRequest(
        string text,
        int maximumSegmentCharacters,
        int overlapCharacters,
        int maximumSummaryCharacters,
        int maximumKeywords,
        int minimumKeywordLength = 3,
        IReadOnlyCollection<string>? excludedTerms =
            null)
    {
        var summarizationRequest =
            new LongTextSummarizationRequest(
                text,
                maximumSegmentCharacters,
                overlapCharacters,
                maximumSummaryCharacters);

        var keywordRequest =
            new TextKeywordExtractionRequest(
                text,
                maximumKeywords,
                minimumKeywordLength,
                excludedTerms);

        Text = summarizationRequest.Text;
        MaximumSegmentCharacters =
            summarizationRequest
                .MaximumSegmentCharacters;
        OverlapCharacters =
            summarizationRequest.OverlapCharacters;
        MaximumSummaryCharacters =
            summarizationRequest
                .MaximumSummaryCharacters;
        MaximumKeywords =
            keywordRequest.MaximumKeywords;
        MinimumKeywordLength =
            keywordRequest.MinimumKeywordLength;
        ExcludedTerms =
            keywordRequest.ExcludedTerms.ToArray();
    }

    public string Text { get; }

    public int MaximumSegmentCharacters { get; }

    public int OverlapCharacters { get; }

    public int MaximumSummaryCharacters { get; }

    public int MaximumKeywords { get; }

    public int MinimumKeywordLength { get; }

    public IReadOnlyList<string> ExcludedTerms
    { get; }
}
