namespace EMF.Intelligence.Capabilities;

public sealed class TextInsightRequest
{
    public TextInsightRequest(
        string text,
        int maximumSummaryCharacters,
        int maximumKeywords,
        int minimumKeywordLength = 3,
        IReadOnlyCollection<string>? excludedTerms =
            null)
    {
        var summarizationRequest =
            new TextSummarizationRequest(
                text,
                maximumSummaryCharacters);

        var keywordRequest =
            new TextKeywordExtractionRequest(
                text,
                maximumKeywords,
                minimumKeywordLength,
                excludedTerms);

        Text = summarizationRequest.Text;
        MaximumSummaryCharacters =
            summarizationRequest.MaximumCharacters;
        MaximumKeywords =
            keywordRequest.MaximumKeywords;
        MinimumKeywordLength =
            keywordRequest.MinimumKeywordLength;
        ExcludedTerms =
            keywordRequest.ExcludedTerms.ToArray();
    }

    public string Text { get; }

    public int MaximumSummaryCharacters { get; }

    public int MaximumKeywords { get; }

    public int MinimumKeywordLength { get; }

    public IReadOnlyList<string> ExcludedTerms
    { get; }
}
