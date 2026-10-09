using EMF.Core.Models.Identities;
namespace EMF.Core.Contracts.Storage;

/// <summary>Borrowed views/streams must not outlive the owner. Escaped memory cannot be revoked;
/// disposal zeros it. Dispose streams before this lease; stream disposal does not clear the owner.</summary>
public interface IArtifactContentReadLease : IDisposable, IAsyncDisposable
{
    ArtifactId ArtifactId { get; }
    ArtifactContentRevision Revision { get; }
    long StoredLength { get; }
    long ReturnedLength { get; }
    ReadOnlyMemory<byte> Content { get; }
    Stream OpenReadStream();
}
