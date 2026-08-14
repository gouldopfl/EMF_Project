namespace EMF.Intelligence.Capabilities;

public sealed class TextSegment
{
    public TextSegment(
        int index,
        int startOffset,
        string text)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index));
        }

        if (startOffset < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startOffset));
        }

        ArgumentException.ThrowIfNullOrEmpty(
            text);

        Index = index;
        StartOffset = startOffset;
        Text = text;
    }

    public int Index { get; }

    public int StartOffset { get; }

    public int EndOffset =>
        StartOffset + Text.Length;

    public string Text { get; }
}
