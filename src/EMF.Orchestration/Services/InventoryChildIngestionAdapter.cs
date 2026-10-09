using System.Security.Cryptography;
using System.Text.Json;
using EMF.Core.Contracts;
using EMF.Core.Contracts.Ingestion;
using EMF.Core.Models;
using EMF.Security.Ingestion;
using EMF.Security.Authorization;
using EMF.Security.Auditing;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Models.Identities;
using EMF.Core.Contracts.Storage;
using EMF.Inventory.Models;
using EMF.Orchestration.Contracts;

namespace EMF.Orchestration.Services;

// The host must compose its existing security context/classification/authorization/audit
// services for this exact child and revalidate the immutable admission revision at adoption.
public sealed record InventoryAuthenticatedChild(IArtifactIngestionPersistence Persistence,
    IVersionedArtifactContentStore Physical, IArtifactContentStagingStore Staging, IEnvelopeEncryptionService Encryption,
    IArtifactIngestionSecurityContext SecurityContext, IArtifactIngestionClassificationPolicy Classification,
    IAuthorizationPolicy Authorization, IAcknowledgedSecurityAuditSink Audit, IContentFingerprintService Fingerprints);

public sealed class InventoryChildIngestionAdapter : IInventoryChildIngestionAdapter
{
    private readonly Func<InventoryAuthorityBinding?, CancellationToken, Task<InventoryAuthorityBinding>> _validate;
    private readonly Func<InventoryParentPlan, InventoryPlanItem, CancellationToken, Task<InventoryAuthenticatedChild>> _compose;
    public InventoryChildIngestionAdapter(
        Func<InventoryAuthorityBinding?, CancellationToken, Task<InventoryAuthorityBinding>> validate,
        Func<InventoryParentPlan, InventoryPlanItem, CancellationToken, Task<InventoryAuthenticatedChild>> compose)
    { ArgumentNullException.ThrowIfNull(validate); ArgumentNullException.ThrowIfNull(compose); (_validate, _compose) = (validate, compose); }
    public async Task<InventoryAuthorityBinding> ValidateAuthorityAsync(InventoryAuthorityBinding? expected, CancellationToken ct)
    {
        var current = await _validate(expected, ct).ConfigureAwait(false);
        if (current is null || string.IsNullOrWhiteSpace(current.AuthorityId) || string.IsNullOrWhiteSpace(current.Revision) ||
            string.IsNullOrWhiteSpace(current.ActorId) || (expected is not null && expected != current))
            throw new UnauthorizedAccessException("Inventory admission authority is unavailable or stale.");
        return current;
    }
    public async Task<InventoryChildConfirmation> ExecuteAsync(InventoryParentPlan parent, InventoryPlanItem child,
        Func<CancellationToken, Task<byte[]>> readContent, CancellationToken ct)
    {
        if (child.Ordinal < 0 || child.Ordinal >= parent.Items.Count || parent.Items[child.Ordinal] != child)
            throw new InvalidDataException("Child is not in the admitted Inventory plan.");
        if (parent.Mode != InventoryMode.ProtectedContent || parent.Authority is null) throw new UnauthorizedAccessException();
        await ValidateAuthorityAsync(parent.Authority, ct).ConfigureAwait(false);
        var runtime = await _compose(parent, child, ct).ConfigureAwait(false);
        var operation = new ArtifactContentOperationId(child.ChildOperationId);
        var authenticated = await runtime.SecurityContext.GetIngestionOperationAsync(ct).ConfigureAwait(false);
        if (authenticated.OperationId != operation || authenticated.ParentOperationId?.Value != parent.ParentId || authenticated.ActorId != parent.Authority.ActorId)
            throw new UnauthorizedAccessException("Authenticated child operation/parent/actor binding mismatch.");
        var coordinator = new ArtifactIngestionCoordinator(runtime.Persistence, runtime.Physical, runtime.Staging, runtime.Encryption,
            runtime.SecurityContext, new RevisionClassification(runtime.Classification, token => ValidateAuthorityAsync(parent.Authority, token)),
            runtime.Authorization, runtime.Audit, runtime.Fingerprints);
        bool resume;
        await using (var session = await runtime.Persistence.AcquireAsync(operation, ct).ConfigureAwait(false))
        {
            if (session.OperationId != operation) throw new InvalidDataException("Child persistence operation mismatch.");
            if (session.IsDamaged || session.HasReview) throw new InventoryChildRejectedException("Child ingestion requires review.");
            resume = session.Intent is not null;
            var safeUnstarted = session.Intent is { State: ArtifactIngestionState.Prepared, CandidateHash: null } && !session.CandidatePreparationStarted;
            if (resume && (session.OperationBinding?.OperationId != authenticated.AuthorizedOperationId ||
                session.OperationBinding.OriginalActorId != authenticated.ActorId || session.OperationBinding.ParentOperationId != authenticated.ParentOperationId))
                throw new UnauthorizedAccessException("Authenticated child durable binding changed.");
            if (resume && session.Intent!.ArtifactId.Value != child.ArtifactId) throw new InvalidDataException("Child binding changed.");
            if (resume)
            {
                var stored = await session.ReadDraftAsync(ct).ConfigureAwait(false);
                if (stored is null || JsonSerializer.Serialize(stored) != JsonSerializer.Serialize(JsonSerializer.Deserialize<IngestionMetadataDraft>(child.DraftJson))) throw new InvalidDataException("Child request binding changed.");
            }
            if (safeUnstarted) resume = false;
        }
        ArtifactIngestionOutcome outcome;
        if (resume) outcome = await coordinator.ResumeAsync(ct).ConfigureAwait(false);
        else
        {
            var draft = JsonSerializer.Deserialize<IngestionMetadataDraft>(child.DraftJson) ?? throw new InvalidDataException();
            var bytes = await readContent(ct).ConfigureAwait(false);
            try { outcome = await coordinator.IngestAsync(draft, bytes, ct).ConfigureAwait(false); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        await ValidateAuthorityAsync(parent.Authority, ct).ConfigureAwait(false);
        if (outcome.State == ArtifactIngestionState.RequiresReview || outcome.State == ArtifactIngestionState.Cleaned && outcome.Disposition == ArtifactIngestionDisposition.None)
            throw new InventoryChildRejectedException("Child lifecycle terminally rejected Inventory adoption.");
        if (outcome.OperationId != operation || outcome.Result is null || outcome.AuditDelivery != IngestionAuditDelivery.Completed)
            throw new InvalidOperationException("Child ingestion has no completed durable outcome.");
        await using var proof = await runtime.Persistence.AcquireAsync(operation, ct).ConfigureAwait(false);
        var intent = proof.Intent ?? throw new InvalidDataException("Missing child intent.");
        if (intent.ArtifactId.Value != child.ArtifactId || !ValidFinalState(intent) ||
            intent.Disposition != outcome.Disposition || proof.HasReview || proof.IsDamaged)
            throw new InvalidDataException("Child confirmation evidence mismatch.");
        var result = await proof.ReadResultAsync(ct).ConfigureAwait(false);
        if (result is null || result.Artifact.Id != outcome.Result.Artifact.Id ||
            !await proof.HasAdoptionEvidenceAsync(result.Artifact.Id, ct).ConfigureAwait(false))
            throw new InvalidDataException("Missing durable adoption evidence.");
        var disposition = intent.Disposition switch
        {
            ArtifactIngestionDisposition.ProvisionalArtifactAdopted => InventoryConfirmationDisposition.ContentAdopted,
            ArtifactIngestionDisposition.DeduplicatedToCanonicalArtifact => InventoryConfirmationDisposition.CanonicalDeduplicated,
            _ => throw new InvalidDataException("Child disposition cannot confirm Inventory.")
        };
        return new(child.Ordinal, child.ChildOperationId, disposition, result.Artifact.Id.Value,
            $"ingestion:{operation.Value}:{intent.Revision}:{intent.Disposition}");
    }
    private static bool ValidFinalState(ArtifactIngestionIntent intent) => intent.Disposition switch
    {
        ArtifactIngestionDisposition.ProvisionalArtifactAdopted => intent.State == ArtifactIngestionState.Completed,
        ArtifactIngestionDisposition.DeduplicatedToCanonicalArtifact => intent.CanonicalArtifactId is not null && intent.State is ArtifactIngestionState.CleanupClaimed or ArtifactIngestionState.Cleaned,
        _ => false
    };
    // Adds only the admission revision fence; authoritative classification decisions remain unchanged.
    private sealed class RevisionClassification(IArtifactIngestionClassificationPolicy inner,
        Func<CancellationToken, Task<InventoryAuthorityBinding>> validate) : IArtifactIngestionClassificationPolicy
    {
        public async Task<ProtectionClassificationId?> ResolveProvisionalAsync(AuthenticatedIngestionOperation operation, CancellationToken ct = default)
        { await validate(ct).ConfigureAwait(false); return await inner.ResolveProvisionalAsync(operation, ct).ConfigureAwait(false); }
        public async Task<bool> CanAdoptAsync(IngestionClassificationAuthority authority, Artifact proposed, CancellationToken ct = default)
        { var allowed = await inner.CanAdoptAsync(authority, proposed, ct).ConfigureAwait(false); await validate(ct).ConfigureAwait(false); return allowed; }
        public async Task<bool> CanonicalClassificationAgreesAsync(IngestionClassificationAuthority provisional, IngestionClassificationAuthority canonical, CancellationToken ct = default)
        { var allowed = await inner.CanonicalClassificationAgreesAsync(provisional, canonical, ct).ConfigureAwait(false); await validate(ct).ConfigureAwait(false); return allowed; }
    }
}
