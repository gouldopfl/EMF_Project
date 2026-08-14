namespace EMF.Intelligence.Capabilities;

public sealed class LongTextSummarizationRequest
{
    public LongTextSummarizationRequest(
        string text,
        int maximumSegmentCharacters,
        int overlapCharacters,
        int maximumSummaryCharacters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            text);

        if (maximumSegmentCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumSegmentCharacters));
        }

        if (overlapCharacters < 0 ||
            overlapCharacters >=
                maximumSegmentCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(overlapCharacters));
        }

        if (maximumSummaryCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumSummaryCharacters));
        }

        Text = text;
        MaximumSegmentCharacters =
            maximumSegmentCharacters;
        OverlapCharacters = overlapCharacters;
        MaximumSummaryCharacters =
            maximumSummaryCharacters;
    }

    public string Text { get; }

    public int MaximumSegmentCharacters { get; }

    public int OverlapCharacters { get; }

    public int MaximumSummaryCharacters { get; }
}
