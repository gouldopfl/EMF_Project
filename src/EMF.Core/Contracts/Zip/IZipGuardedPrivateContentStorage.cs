using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
namespace EMF.Core.Contracts.Zip;

// ZIP-only capability; absence is a denial, never permission to use the raw methods.
public interface IZipGuardedPrivateContentStorage : IZipPrivateContentStorage
{
    Task StageGuardedAsync(ArtifactContentOperationId operation, ReadOnlyMemory<byte> candidate, IZipEffectBoundaryGuard guard, CancellationToken ct = default);
    Task<byte[]?> ReadCandidateGuardedAsync(ArtifactContentOperationId operation, IZipEffectBoundaryGuard guard, CancellationToken ct = default);
    Task<ArtifactContentMutationReceipt?> GetReceiptGuardedAsync(ArtifactContentOperationId operation, IZipEffectBoundaryGuard guard, CancellationToken ct = default);
    Task<IPreparedArtifactContentMutation> PrepareCreateGuardedAsync(ArtifactId id, ReadOnlyMemory<byte> candidate, ArtifactContentMutationContext context, IZipEffectBoundaryGuard guard, CancellationToken ct = default);
    Task<IArtifactContentReadLease?> ReadProtectedGuardedAsync(ArtifactId id, ArtifactContentRevision expected, IZipEffectBoundaryGuard guard, CancellationToken ct = default);
}
