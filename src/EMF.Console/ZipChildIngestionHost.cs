using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts.Zip;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Contracts;
using EMF.Persistence.Repositories;
using EMF.Security.Auditing;
using EMF.Security.Authorization;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Ingestion;
namespace EMF.ConsoleApplication;

// Explicit governed host dependencies. No fallback actor, classification or authority
// may be inferred from CLI arguments, names, MIME/extension guesses or ZIP metadata.
public sealed class ZipChildIngestionHost(string evidenceDatabasePath,IVersionedArtifactContentStore physical,
    IArtifactContentStagingStore staging,IEnvelopeEncryptionService encryption,
    Func<string,CancellationToken,Task<IArtifactIngestionSecurityContext>> authenticatedContexts,
    IArtifactIngestionClassificationPolicy classification,IAuthorizationPolicy authorization,
    IAcknowledgedSecurityAuditSink audit,IContentFingerprintService fingerprints):IZipGuardedChildIngestionHost
{
    public async Task<ZipChildIngestionRuntime> OpenAsync(ZipParentSnapshot parent,ZipEntryProgress entry,CancellationToken ct=default)
    {
        var source=await authenticatedContexts(entry.Plan.ChildOperationId!,ct)??throw new UnauthorizedAccessException("Missing authenticated ZIP child context.");
        var operation=await source.GetIngestionOperationAsync(ct);
        if(operation.OperationId.Value!=entry.Plan.ChildOperationId||operation.ParentOperationId?.Value!=parent.Binding.OperationId)
            throw new UnauthorizedAccessException("Authenticated ZIP child operation binding changed.");
        var context=new FrozenContext(source,operation);
        var persistence=new ZipFencedIngestionPersistence(evidenceDatabasePath,parent.Fence,entry);
        return new(new(persistence,physical,staging,encryption,context,classification,authorization,audit,fingerprints),persistence);
    }
    public async Task<ZipChildIngestionRuntime> OpenGuardedAsync(ZipParentSnapshot parent, ZipEntryProgress entry,
        IZipChildIngestionEffectGate gate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(gate);
        ValidateGuardedDependencies();
        var prepared = (IPreparedArtifactContentStore)physical;
        var source = await authenticatedContexts(entry.Plan.ChildOperationId!, ct)
            ?? throw new UnauthorizedAccessException("Missing authenticated ZIP child context.");
        var operation = await source.GetIngestionOperationAsync(ct);
        if (operation.OperationId.Value != entry.Plan.ChildOperationId ||
            operation.ParentOperationId?.Value != parent.Binding.OperationId)
            throw new UnauthorizedAccessException("Authenticated ZIP child operation binding changed.");
        var persistence = new ZipGuardedIngestionPersistence(
            new ZipFencedIngestionPersistence(evidenceDatabasePath, parent.Fence, entry, gate.Clock), gate);
        var coordinator = new ArtifactIngestionCoordinator(persistence,
            new ZipGuardedIngestionPhysical(prepared, gate), new ZipGuardedIngestionStaging(staging, gate),
            new ZipGuardedIngestionEncryption(encryption, gate), new FrozenContext(source, operation),
            classification, authorization, new ZipGuardedIngestionAudit(audit, gate), fingerprints);
        return new(coordinator, persistence, gate);
    }
    public void ValidateGuardedDependencies()
    {
        // Call before the first durable reservation. The legacy host never
        // silently substitutes for a guarded provider.
        if (physical is not IPreparedArtifactContentStore prepared ||
            prepared is not IZipGuardedPreparedArtifactContentStore ||
            staging is not IZipGuardedArtifactContentStagingStore ||
            encryption is not IZipGuardedEnvelopeEncryptionService ||
            audit is not IZipGuardedAcknowledgedSecurityAuditSink)
            throw new NotSupportedException("Guarded ZIP child ingestion dependencies are unavailable.");
    }
    private sealed class FrozenContext(IArtifactIngestionSecurityContext source,AuthenticatedIngestionOperation operation):IArtifactIngestionSecurityContext
    {
        public Task<AuthenticatedIngestionOperation> GetIngestionOperationAsync(CancellationToken ct=default){ct.ThrowIfCancellationRequested();return Task.FromResult(operation);}
        public Task<string> GetRecoveryActorAsync(CancellationToken ct=default)=>source.GetRecoveryActorAsync(ct);
        public Task<bool> AuthorizeNonDestructiveReviewAsync(string actor,ArtifactContentOperationId id,ArtifactId? artifact,CancellationToken ct=default)=>source.AuthorizeNonDestructiveReviewAsync(actor,id,artifact,ct);
    }
}
