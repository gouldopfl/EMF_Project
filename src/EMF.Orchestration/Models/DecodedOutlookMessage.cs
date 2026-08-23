namespace EMF.Orchestration.Models;

public sealed class DecodedOutlookMessage
{
    public string? Subject { get; init; }

    public string? BodyText { get; init; }

    public string? BodyHtml { get; init; }

    public IReadOnlyList<DecodedOutlookAttachment> Attachments
    { get; init; } = Array.Empty<DecodedOutlookAttachment>();
}
