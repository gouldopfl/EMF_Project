namespace EMF.Core.Contracts.Storage;
// Encrypted serialized candidates only. The provider is cryptography-unaware;
// staging is private, crash durable, independent of generation garbage collection.
public interface IArtifactContentStagingStore
{
    Task StageAsync(ArtifactContentOperationId operationId, ReadOnlyMemory<byte> encryptedCandidate, CancellationToken cancellationToken = default);
    Task<byte[]?> ReadAsync(ArtifactContentOperationId operationId, CancellationToken cancellationToken = default);
}
