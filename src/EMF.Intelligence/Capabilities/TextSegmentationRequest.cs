namespace EMF.Intelligence.Capabilities;

public sealed class TextSegmentationRequest
{
    public TextSegmentationRequest(
        string text,
        int maximumSegmentCharacters,
        int overlapCharacters = 0)
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

        Text = text;
        MaximumSegmentCharacters =
            maximumSegmentCharacters;
        OverlapCharacters = overlapCharacters;
    }

    public string Text { get; }

    public int MaximumSegmentCharacters { get; }

    public int OverlapCharacters { get; }
}
