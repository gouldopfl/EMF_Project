namespace EMF.Discovery.Models.Email;

public sealed class EmailAttachment
{
    public required string FileName { get; init; }

    public string? ContentType { get; init; }

    public long? SizeBytes { get; init; }

    public string? ContentId { get; init; }

    public bool IsInline { get; init; }
}
