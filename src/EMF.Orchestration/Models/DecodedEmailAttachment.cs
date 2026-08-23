namespace EMF.Orchestration.Models;

public sealed class DecodedEmailAttachment
{
    public required string FileName { get; init; }

    public string? ContentType { get; init; }

    public string? ContentId { get; init; }

    public bool IsInline { get; init; }

    public required byte[] Content { get; init; }
}
