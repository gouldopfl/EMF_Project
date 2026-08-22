namespace EMF.Discovery.Models.Email;

public sealed class EmailMessage
{
    public string? MessageId { get; init; }

    public string? From { get; init; }

    public IReadOnlyList<string> To { get; init; } =
        Array.Empty<string>();

    public IReadOnlyList<string> Cc { get; init; } =
        Array.Empty<string>();

    public IReadOnlyList<string> Bcc { get; init; } =
        Array.Empty<string>();

    public string? Subject { get; init; }

    public DateTimeOffset? DateUtc { get; init; }

    public string? TextBody { get; init; }

    public string? HtmlBody { get; init; }

    public string SourceFormat { get; init; } = string.Empty;

    public IReadOnlyList<EmailAttachment> Attachments { get; init; } =
        Array.Empty<EmailAttachment>();
}
