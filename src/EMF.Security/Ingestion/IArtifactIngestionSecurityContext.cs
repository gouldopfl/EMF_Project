using EMF.Core.Contracts.Ingestion;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Security.Models.Identities;

namespace EMF.Security.Ingestion;

// Implemented by the authenticated host, not populated from ingestion request fields.
// A retry returns the same logical operation and authorized-operation binding.
public sealed record AuthenticatedIngestionOperation(ArtifactContentOperationId OperationId,
    IngestionAuthorizedOperationId AuthorizedOperationId, string ActorId);
public interface IArtifactIngestionSecurityContext
{
    Task<AuthenticatedIngestionOperation> GetIngestionOperationAsync(CancellationToken cancellationToken = default);
    Task<string> GetRecoveryActorAsync(CancellationToken cancellationToken = default);
    // Separate governance permission for non-destructive review when ordinary
    // classification authority is absent. Must not authorize content access/delete.
    Task<bool> AuthorizeNonDestructiveReviewAsync(string executingActorId, ArtifactContentOperationId operationId,
        ArtifactId? artifactId, CancellationToken cancellationToken = default);
}

// Governed classification policy is authoritative. Unknown classification must return null.
// Proposed metadata is reconciled against this authority, never used to establish it.
public interface IArtifactIngestionClassificationPolicy
{
    Task<ProtectionClassificationId?> ResolveProvisionalAsync(AuthenticatedIngestionOperation operation, CancellationToken cancellationToken = default);
    Task<bool> CanAdoptAsync(IngestionClassificationAuthority authority, Artifact proposedArtifact, CancellationToken cancellationToken = default);
    Task<bool> CanonicalClassificationAgreesAsync(IngestionClassificationAuthority provisional, IngestionClassificationAuthority canonical, CancellationToken cancellationToken = default);
}
