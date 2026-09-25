namespace EMF.Core.Models;

public sealed class PrintableArtifactPage
{
    public required int PageNumber
    { get; init; }

    public required string ContentType
    { get; init; }

    public required ReadOnlyMemory<byte> Content
    { get; init; }

    /// <summary>
    /// Optional whole-page presentation rotation, in clockwise degrees, inferred
    /// from native text geometry after the source's own rotation is applied.
    /// The preserved page content is unchanged. Consumers may opt into this hint.
    /// </summary>
    public int SuggestedClockwiseRotation { get; init; }

    public PrintableArtifactTextGeometry? TextGeometry { get; init; }
}
