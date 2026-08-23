namespace EMF.Orchestration.Models;

public sealed class DecodedOutlookAttachment
{
    public required string FileName { get; init; }

    public required byte[] Content { get; init; }

    public string? ContentId { get; init; }

    public bool IsInline { get; init; }
}
