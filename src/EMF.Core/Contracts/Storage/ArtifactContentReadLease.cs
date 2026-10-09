using System.Security.Cryptography;
using EMF.Core.Models.Identities;
namespace EMF.Core.Contracts.Storage;

/// <summary>Takes ownership of the supplied array without copying. The caller must relinquish it.</summary>
public sealed class ArtifactContentReadLease : IArtifactContentReadLease
{
    private byte[]? _owned;
    private readonly ArtifactId _id;
    private readonly ArtifactContentRevision _revision;
    private readonly long _storedLength;
    public ArtifactContentReadLease(ArtifactId artifactId, ArtifactContentRevision revision,
        long storedLength, byte[] ownedContent)
    {
        ArgumentNullException.ThrowIfNull(ownedContent);
        _ = new ArtifactId(artifactId.Value);
        ArtifactContentIdentity.Validate(revision.Value);
        if (storedLength < 0 || storedLength > Array.MaxLength) throw new ArgumentOutOfRangeException(nameof(storedLength));
        _id = artifactId; _revision = revision; _storedLength = storedLength; _owned = ownedContent;
    }
    private byte[] Owned => _owned ?? throw new ObjectDisposedException(nameof(ArtifactContentReadLease));
    public ArtifactId ArtifactId { get { _ = Owned; return _id; } }
    public ArtifactContentRevision Revision { get { _ = Owned; return _revision; } }
    public long StoredLength { get { _ = Owned; return _storedLength; } }
    public long ReturnedLength => Owned.Length;
    public ReadOnlyMemory<byte> Content => Owned;
    public Stream OpenReadStream() => new MemoryStream(Owned, 0, Owned.Length, writable: false, publiclyVisible: false);
    public void Dispose()
    {
        var owned = Interlocked.Exchange(ref _owned, null);
        if (owned is not null) CryptographicOperations.ZeroMemory(owned);
    }
    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
}
