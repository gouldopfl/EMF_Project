using EMF.Core.Models.Identities;

namespace EMF.Core.Contracts.Storage;

public interface IVersionedArtifactContentStore : IArtifactContentStore
{
    Task<ArtifactContentSnapshot?> ReadVersionedAsync(ArtifactId id, CancellationToken cancellationToken = default);
    Task<ArtifactContentMutationResult> CreateIfAbsentAsync(ArtifactId id, ReadOnlyMemory<byte> content,
        ArtifactContentMutationContext context, CancellationToken cancellationToken = default);
    Task<ArtifactContentMutationResult> ReplaceIfRevisionMatchesAsync(ArtifactId id, ArtifactContentRevision expected,
        ReadOnlyMemory<byte> content, ArtifactContentMutationContext context, CancellationToken cancellationToken = default);
    Task<ArtifactContentMutationResult> DeleteIfRevisionMatchesAsync(ArtifactId id, ArtifactContentRevision expected,
        ArtifactContentMutationContext context, CancellationToken cancellationToken = default);
    Task<ArtifactContentMutationReceipt?> GetMutationOutcomeAsync(ArtifactContentOperationId operationId,
        CancellationToken cancellationToken = default);
    // Independent receipt enumeration supports later journal-loss reconciliation.
    // Cursors are store-scoped, durable commit order; later commits cannot sort behind a cursor.
    // No audit delivery/ledger semantics are implemented by the physical store.
    Task<IReadOnlyList<ArtifactContentAuditObligation>> ReadAuditObligationsAsync(
        ArtifactContentReceiptCursor? afterCursor, int limit, CancellationToken cancellationToken = default);
}
