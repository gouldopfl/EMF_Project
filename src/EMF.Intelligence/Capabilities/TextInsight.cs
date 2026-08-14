namespace EMF.Intelligence.Capabilities;

public sealed class TextInsight
{
    public TextInsight(
        string summary,
        IReadOnlyCollection<TextKeyword> keywords)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            summary);
        ArgumentNullException.ThrowIfNull(keywords);

        var configuredKeywords = keywords.ToArray();

        if (configuredKeywords.Any(
                keyword => keyword is null))
        {
            throw new ArgumentException(
                "Keywords cannot contain null.",
                nameof(keywords));
        }

        Summary = summary;
        Keywords = configuredKeywords;
    }

    public string Summary { get; }

    public IReadOnlyList<TextKeyword> Keywords
    { get; }
}
