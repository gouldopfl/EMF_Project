namespace EMF.Intelligence.Capabilities;

public sealed class TextSummarizationRequest
{
    public TextSummarizationRequest(
        string text,
        int maximumCharacters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            text);

        if (maximumCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumCharacters),
                "Maximum characters must be positive.");
        }

        Text = text;
        MaximumCharacters = maximumCharacters;
    }

    public string Text { get; }

    public int MaximumCharacters { get; }
}
