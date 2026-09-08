namespace EMF.Core.Models;

public sealed class PrintableArtifactPage
{
    public required int PageNumber
    { get; init; }

    public required string ContentType
    { get; init; }

    public required ReadOnlyMemory<byte> Content
    { get; init; }
}
