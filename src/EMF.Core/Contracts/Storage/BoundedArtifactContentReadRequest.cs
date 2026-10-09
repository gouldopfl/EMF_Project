namespace EMF.Core.Contracts.Storage;

/// <summary>Bounds a selected-current read; ExpectedRevision never requests historical content.</summary>
public sealed class BoundedArtifactContentReadRequest
{
    public long MaximumStoredRepresentationBytes { get; }
    public long MaximumReturnedContentBytes { get; }
    public ArtifactContentRevision? ExpectedRevision { get; }
    public BoundedArtifactContentReadRequest(long maximumStoredRepresentationBytes,
        long maximumReturnedContentBytes, ArtifactContentRevision? expectedRevision = null)
    {
        ValidateSize(maximumStoredRepresentationBytes);
        ValidateSize(maximumReturnedContentBytes);
        if (expectedRevision is { } revision) ArtifactContentIdentity.Validate(revision.Value);
        MaximumStoredRepresentationBytes = maximumStoredRepresentationBytes;
        MaximumReturnedContentBytes = maximumReturnedContentBytes;
        ExpectedRevision = expectedRevision;
    }
    private static void ValidateSize(long value)
    {
        if (value <= 0 || value > Array.MaxLength) throw new ArgumentOutOfRangeException(nameof(value));
        _ = checked((int)value);
    }
}
