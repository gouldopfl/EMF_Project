using EMF.Core.Contracts.Zip;
using Microsoft.Data.Sqlite;

namespace EMF.Persistence.Repositories;

public sealed partial class SqliteZipExtractionJournal
{
    private static void GuardedPrepared(EMF.Core.Contracts.Storage.IPreparedArtifactContentMutation? prepared,
        EMF.Core.Contracts.Storage.ArtifactContentMutationReceipt? known, IZipEffectBoundaryGuard guard)
    {
        if (known is null && (prepared is not IZipGuardedPreparedContentMutation guarded ||
            !ReferenceEquals(guarded.BoundaryGuard, guard)))
            throw new UnauthorizedAccessException("ZIP promotion requires the same guarded physical preparation.");
    }

    private async Task CommitEffectAsync(SqliteTransaction tx, IZipEffectBoundaryGuard? guard,
        CancellationToken ct, DateTimeOffset? ownerUntil = null, DateTimeOffset? evidenceUntil = null, Func<Task<DateTimeOffset>>? admissionEligibility = null)
    {
        if (guard is null) { tx.Commit(); return; }
        if (admissionEligibility is not null) evidenceUntil = await admissionEligibility();
        await guard.RevalidateAsync(ct);
        ct.ThrowIfCancellationRequested();
        guard.Invalidated.ThrowIfCancellationRequested();
        using var acceptance = guard.Accept(ct) ?? throw new UnauthorizedAccessException("ZIP guard returned no acceptance capability.");
        ct.ThrowIfCancellationRequested();
        guard.Invalidated.ThrowIfCancellationRequested();
        if (ownerUntil is { } until && until <= _time.GetUtcNow()) throw new ZipFenceException();
        if (evidenceUntil is { } evidenceExpiry && evidenceExpiry <= _time.GetUtcNow())
            throw new UnauthorizedAccessException("Recorded ZIP approval expired; issuer revalidation is required.");
        tx.Commit();
    }

    // Permission can only veto eligible durable work. A live root cannot reopen
    // an admission stopped independently of that root's invalidation signal.
    private async Task<DateTimeOffset> ValidateExecutionAdmissionAsync(SqliteConnection c, SqliteTransaction tx,
        ZipParentSnapshot parent, CancellationToken ct)
    {
        var admission = await ReadAdmissionCoreAsync(c, tx, parent.Binding.OperationId, ct)
            ?? throw new UnauthorizedAccessException("Guarded ZIP execution requires current admission evidence.");
        if (admission.State != ZipAdmissionState.ParentBound || admission.Parent != parent.Binding)
            throw new UnauthorizedAccessException("Current ZIP admission does not permit parent execution.");
        var retained = await ParentRetentionAsync(c, tx, parent.Binding.OperationId, ct)
            ?? throw new InvalidDataException("Missing admitted ZIP parent retention.");
        ParentBinding(parent, retained);
        if (admission.Source != retained.Identity.Source || admission.Binding.ParentNamespaceId != retained.Identity.NamespaceId)
            throw new InvalidDataException("Current ZIP admission differs from retained parent identity.");
        return CurrentEvidenceExpiry(admission);
    }

    private DateTimeOffset CurrentEvidenceExpiry(ZipParentAdmission admission)
    {
        if (admission.LatestAuthorityEvidence is null || admission.LatestAuthorityEvidence.ExpiresUtc <= _time.GetUtcNow())
            throw new UnauthorizedAccessException("Current ZIP admission evidence expired.");
        return admission.LatestAuthorityEvidence.ExpiresUtc;
    }

    private async Task<DateTimeOffset> ValidateInitialRetentionAdmissionAsync(SqliteConnection c, SqliteTransaction tx,
        string operation, string parentArtifact, ZipRetainedBinding source, string profile, string namespaceId, CancellationToken ct)
    {
        var admission = await ReadAdmissionCoreAsync(c, tx, operation, ct)
            ?? throw new UnauthorizedAccessException("Guarded ZIP retention requires current admission evidence.");
        if (admission.State != ZipAdmissionState.SourceBound || admission.Source != source ||
            admission.Binding.ParentArtifactId != parentArtifact || admission.Binding.ProfileHash != profile ||
            admission.Binding.ParentNamespaceId != namespaceId)
            throw new UnauthorizedAccessException("Current ZIP admission does not permit this exact initial retention.");
        return CurrentEvidenceExpiry(admission);
    }

    private Task<DateTimeOffset> ValidateInitialRetentionAdmissionAsync(SqliteConnection c, SqliteTransaction tx,
        ZipParentRetentionIdentity identity, CancellationToken ct) => ValidateInitialRetentionAdmissionAsync(c, tx,
            identity.ParentOperationId, identity.ParentArtifactId, identity.Source, identity.ProfileHash, identity.NamespaceId, ct);

    private async Task<DateTimeOffset> ValidateParentCreateAdmissionAsync(SqliteConnection c, SqliteTransaction tx,
        ZipParentBinding binding, CancellationToken ct)
    {
        var retained = await ParentRetentionAsync(c, tx, binding.OperationId, ct)
            ?? throw new InvalidDataException("Parent establishment requires exact retained evidence.");
        if (retained.State != ZipRetentionState.Created || retained.CreateReceipt?.CurrentRevision is null ||
            binding != new ZipParentBinding(retained.Identity.ParentOperationId, retained.Identity.ParentArtifactId,
                new(retained.Identity.ObjectId, retained.CreateReceipt.CurrentRevision.Value.Value,
                    retained.Identity.Source.Sha256, retained.Identity.Source.Length), retained.Identity.ProfileHash))
            throw new InvalidDataException("Parent establishment differs from created retention.");
        return await ValidateInitialRetentionAdmissionAsync(c, tx, retained.Identity, ct);
    }

    private static async ValueTask ValidateEffectAsync(IZipEffectBoundaryGuard? guard, CancellationToken ct)
    {
        if (guard is null) return;
        await guard.RevalidateAsync(ct);
        ct.ThrowIfCancellationRequested();
        guard.Invalidated.ThrowIfCancellationRequested();
    }
}
