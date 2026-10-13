using EMF.Core.Contracts.Ingestion;
using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts.Zip;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Contracts;
using EMF.Security.Auditing;
using EMF.Security.Auditing.Models;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Models;
using EMF.Security.Ingestion;

namespace EMF.ConsoleApplication;

// These decorators are used only by the ZIP delegated runtime. Generic ingestion
// callers keep their original dependencies and behavior.
internal static class ZipGuardedChildEffects
{
    internal static async Task<T> RunAsync<T>(IZipChildIngestionEffectGate gate, ZipChildIngestionEffect effect,
        Func<IZipChildIngestionEffectScope, Task<T>> work, CancellationToken ct)
    {
        await using var scope = await gate.OpenAsync(effect, ct);
        var result = await work(scope);
        await scope.FinishAsync(ct);
        return result;
    }
    internal static Task RunAsync(IZipChildIngestionEffectGate gate, ZipChildIngestionEffect effect,
        Func<IZipChildIngestionEffectScope, Task> work, CancellationToken ct)
        => RunAsync(gate, effect, async scope => { await work(scope); return true; }, ct);
}

internal sealed class ZipGuardedIngestionPersistence(IArtifactIngestionPersistence inner,
    IZipChildIngestionEffectGate gate) : IArtifactIngestionPersistence
{
    public async Task<IDisposable> AcquireExecutionAsync(ArtifactContentOperationId id, CancellationToken ct = default)
    {
        await using var scope = await gate.OpenAsync(ZipChildIngestionEffect.Metadata, ct);
        Task<IDisposable> pending;
        using (scope.Accept(ct)) pending = inner.AcquireExecutionAsync(id, ct);
        var lease = await pending;
        try { await scope.FinishAsync(ct); return lease; }
        catch { lease.Dispose(); throw; }
    }
    public async Task<IArtifactIngestionSession> AcquireAsync(ArtifactContentOperationId id, CancellationToken ct = default)
    {
        await using var scope = await gate.OpenAsync(ZipChildIngestionEffect.Metadata, ct);
        Task<IArtifactIngestionSession> pending;
        using (scope.Accept(ct)) pending = inner.AcquireAsync(id, ct);
        var session = await pending;
        try { await scope.FinishAsync(ct); return new Session(session, gate); }
        catch { await session.DisposeAsync(); throw; }
    }
    public Task<IReadOnlyList<IngestionRecoveryWork>> ReadRecoveryWorkAsync(long after, int limit, CancellationToken ct = default)
        => throw new NotSupportedException("ZIP child delegation does not enumerate generic recovery work.");
    public Task<IReadOnlyList<IngestionAuditObligation>> ReadAuditObligationsAsync(ArtifactContentOperationId id, CancellationToken ct = default)
        => ZipGuardedChildEffects.RunAsync(gate, ZipChildIngestionEffect.AuditRead,
            scope => inner.ReadAuditObligationsAsync(id, ct), ct);
    public Task<IngestionAuditObligation?> ReadReceiptAuditObligationAsync(ArtifactContentMutationReceipt receipt, CancellationToken ct = default)
        => ZipGuardedChildEffects.RunAsync(gate, ZipChildIngestionEffect.AuditRead,
            scope => inner.ReadReceiptAuditObligationAsync(receipt, ct), ct);
    public Task<IngestionAuditObligation?> ReadReviewAuditObligationAsync(ArtifactContentOperationId id, CancellationToken ct = default)
        => ZipGuardedChildEffects.RunAsync(gate, ZipChildIngestionEffect.AuditRead,
            scope => inner.ReadReviewAuditObligationAsync(id, ct), ct);
    public Task AcknowledgeAuditAsync(IngestionAuditObligation expected, CancellationToken ct = default)
        => AuditDeliveryAsync(expected, true, ct);
    public Task MarkAuditPendingAsync(IngestionAuditObligation expected, CancellationToken ct = default)
        => AuditDeliveryAsync(expected, false, ct);
    private Task AuditDeliveryAsync(IngestionAuditObligation expected, bool delivered, CancellationToken ct)
    {
        if (inner is not IZipGuardedArtifactIngestionPersistence guarded)
            throw new NotSupportedException("Guarded ingestion audit bookkeeping is required.");
        return ZipGuardedChildEffects.RunAsync(gate, ZipChildIngestionEffect.AuditWrite,
            scope => delivered ? guarded.AcknowledgeAuditGuardedAsync(expected, scope, ct)
                : guarded.MarkAuditPendingGuardedAsync(expected, scope, ct), ct);
    }
    public Task RecordOrphanReceiptAsync(ArtifactContentMutationReceipt receipt, CancellationToken ct = default)
        => throw new NotSupportedException("ZIP child delegation cannot record orphan recovery receipts.");
    public Task<ArtifactContentOperationId?> FindReceiptOperationAsync(ArtifactContentMutationReceipt receipt, CancellationToken ct = default)
        => throw new NotSupportedException("ZIP child delegation cannot enumerate receipt owners.");
    public Task<IngestionRecoveryHealth> ReadRecoveryHealthAsync(CancellationToken ct = default)
        => throw new NotSupportedException("ZIP child delegation cannot enumerate recovery health.");

    private sealed class Session(IArtifactIngestionSession inner, IZipChildIngestionEffectGate gate) : IArtifactIngestionSession
    {
        public ArtifactContentOperationId OperationId => inner.OperationId;
        public ArtifactIngestionIntent? Intent => inner.Intent;
        public IngestionOperationBinding? OperationBinding => inner.OperationBinding;
        public ArtifactId? ProvisionalArtifactId => inner.ProvisionalArtifactId;
        public bool IsDamaged => inner.IsDamaged;
        public bool HasReview => inner.HasReview;
        public bool CandidatePreparationStarted => inner.CandidatePreparationStarted;
        public IngestionCandidateBinding? CandidateBinding => inner.CandidateBinding;
        public ArtifactContentMutationReceipt? CandidateCreationReceipt => inner.CandidateCreationReceipt;
        public Task<bool> HasAdoptionEvidenceAsync(ArtifactId id, CancellationToken ct = default) => inner.HasAdoptionEvidenceAsync(id, ct);
        public Task<IngestionClassificationAuthority?> ResolveAuthorityAsync(ArtifactId id, CancellationToken ct = default) => inner.ResolveAuthorityAsync(id, ct);
        public Task<IngestionMetadataDraft?> ReadDraftAsync(CancellationToken ct = default) => inner.ReadDraftAsync(ct);
        public Task<IngestionMetadataDraft?> FindCanonicalAsync(CancellationToken ct = default) => inner.FindCanonicalAsync(ct);
        public Task<IngestionMetadataDraft?> ReadResultAsync(CancellationToken ct = default) => inner.ReadResultAsync(ct);
        public Task PrepareAsync(ArtifactIngestionIntent intent, IngestionOperationBinding binding, IngestionMetadataDraft draft, CancellationToken ct = default)
            => inner.PrepareAsync(intent, binding, draft, ct);
        public Task BeginCandidatePreparationAsync(ArtifactIngestionIntent expected, CancellationToken ct = default)
            => inner.BeginCandidatePreparationAsync(expected, ct);
        public Task SetCandidateAsync(ArtifactIngestionIntent expected, string hash, CancellationToken ct = default)
            => inner.SetCandidateAsync(expected, hash, ct);
        public Task RecordCreatedAsync(ArtifactIngestionIntent expected, ArtifactContentMutationReceipt receipt, CancellationToken ct = default)
            => inner.RecordCreatedAsync(expected, receipt, ct);
        public Task AdoptAsync(ArtifactIngestionIntent expected, IngestionClassificationAuthority provisional,
            IngestionClassificationAuthority? canonical, string? cleanupActor, CancellationToken ct = default)
            => ZipGuardedChildEffects.RunAsync(gate, ZipChildIngestionEffect.Adoption,
                scope => inner.AdoptAsync(expected, provisional, canonical, cleanupActor, ct), ct);
        public Task ClaimCleanupAsync(ArtifactIngestionIntent expected, IngestionClassificationAuthority authority,
            string actor, CancellationToken ct = default) => inner.ClaimCleanupAsync(expected, authority, actor, ct);
        public Task RecordDeduplicationConflictAsync(ArtifactIngestionIntent expected, IngestionClassificationAuthority authority,
            ArtifactId canonical, string? cleanupActor, CancellationToken ct = default)
            => inner.RecordDeduplicationConflictAsync(expected, authority, canonical, cleanupActor, ct);
        public Task RecordCleanedAsync(ArtifactIngestionIntent expected, ArtifactContentMutationReceipt? receipt,
            string actor, CancellationToken ct = default) => inner.RecordCleanedAsync(expected, receipt, actor, ct);
        public Task RecordReviewAsync(string reason, string actor, CancellationToken ct = default, bool isRecovery = true)
            => inner.RecordReviewAsync(reason, actor, ct, isRecovery);
        public Task EnsureReceiptAuditAsync(ArtifactContentMutationReceipt receipt, CancellationToken ct = default)
            => inner.EnsureReceiptAuditAsync(receipt, ct);
        public Task EnsureAdoptionAuditAsync(CancellationToken ct = default) => inner.EnsureAdoptionAuditAsync(ct);
        public Task RecordRecoveryCompletionAsync(string actor, CancellationToken ct = default)
            => inner.RecordRecoveryCompletionAsync(actor, ct);
        public Task CompleteAsync(ArtifactIngestionIntent expected, CancellationToken ct = default)
            => inner.CompleteAsync(expected, ct);
        public Task ReopenAuditDeliveryAsync(ArtifactIngestionIntent expected, CancellationToken ct = default)
            => inner.ReopenAuditDeliveryAsync(expected, ct);
        public Task CommitAsync(CancellationToken ct = default)
        {
            if (inner is not IZipGuardedArtifactIngestionSession guarded)
                throw new NotSupportedException("Guarded ingestion metadata commit is required.");
            return ZipGuardedChildEffects.RunAsync(gate, ZipChildIngestionEffect.Metadata,
                scope => guarded.CommitGuardedAsync(scope, ct), ct);
        }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}

internal sealed class ZipGuardedIngestionStaging(IArtifactContentStagingStore inner,
    IZipChildIngestionEffectGate gate) : IArtifactContentStagingStore
{
    private IZipGuardedArtifactContentStagingStore Guarded => inner as IZipGuardedArtifactContentStagingStore
        ?? throw new NotSupportedException("Guarded child staging is required.");
    public Task StageAsync(ArtifactContentOperationId id, ReadOnlyMemory<byte> bytes, CancellationToken ct = default)
        => ZipGuardedChildEffects.RunAsync(gate, ZipChildIngestionEffect.CandidateStage,
            scope => Guarded.StageGuardedAsync(id, bytes, scope, ct), ct);
    public Task<byte[]?> ReadAsync(ArtifactContentOperationId id, CancellationToken ct = default)
        => ZipGuardedChildEffects.RunAsync(gate, ZipChildIngestionEffect.CandidateRead,
            scope => Guarded.ReadGuardedAsync(id, scope, ct), ct);
}

internal sealed class ZipGuardedIngestionEncryption(IEnvelopeEncryptionService inner,
    IZipChildIngestionEffectGate gate) : IEnvelopeEncryptionService
{
    private IZipGuardedEnvelopeEncryptionService Guarded => inner as IZipGuardedEnvelopeEncryptionService
        ?? throw new NotSupportedException("Guarded child cryptography is required.");
    public Task<EncryptedEnvelope> EncryptWithContextAsync(ReadOnlyMemory<byte> plain, ReadOnlyMemory<byte> context,
        CancellationToken ct = default)
        => ZipGuardedChildEffects.RunAsync(gate, ZipChildIngestionEffect.Encrypt,
            scope => Guarded.EncryptWithContextGuardedAsync(plain, context, scope, ct), ct);
    public Task<byte[]> DecryptWithContextAsync(EncryptedEnvelope envelope, ReadOnlyMemory<byte> context,
        CancellationToken ct = default)
        => ZipGuardedChildEffects.RunAsync(gate, ZipChildIngestionEffect.Decrypt,
            scope => Guarded.DecryptWithContextBoundedGuardedAsync(envelope, context,
                new EnvelopeDecryptionLimits(ZipNumericLimits.Child), scope, ct), ct);
    public Task<EncryptedEnvelope> EncryptAsync(ReadOnlyMemory<byte> plain, CancellationToken ct = default)
        => throw new NotSupportedException("Unbound encryption is unavailable to ZIP child ingestion.");
    public Task<byte[]> DecryptAsync(EncryptedEnvelope envelope, CancellationToken ct = default)
        => throw new NotSupportedException("Unbound decryption is unavailable to ZIP child ingestion.");
}

internal sealed class ZipGuardedIngestionPhysical(IPreparedArtifactContentStore inner,
    IZipChildIngestionEffectGate gate) : IPreparedArtifactContentStore
{
    private IZipGuardedPreparedArtifactContentStore Guarded => inner as IZipGuardedPreparedArtifactContentStore
        ?? throw new NotSupportedException("Guarded physical child content store is required.");
    private Task<T> Read<T>(Func<Task<T>> work, CancellationToken ct)
        => ZipGuardedChildEffects.RunAsync(gate, ZipChildIngestionEffect.PhysicalRead,
            async scope => { Task<T> pending; using (scope.Accept(ct)) pending = work(); return await pending; }, ct);
    public async Task<ArtifactContentSnapshot?> ReadVersionedAsync(ArtifactId id, CancellationToken ct = default)
    {
        await using var scope = await gate.OpenAsync(ZipChildIngestionEffect.PhysicalRead, ct);
        var max = Math.Min(inner is IBoundedVersionedArtifactContentStore bounded
            ? bounded.MaximumStoredRepresentationBytes : ArtifactIngestionCoordinator.MaximumCandidateBytes,
            ArtifactIngestionCoordinator.MaximumCandidateBytes);
        await using var lease = await Guarded.ReadBoundedVersionedGuardedAsync(id,
            new BoundedArtifactContentReadRequest(max, max), scope, ct);
        var snapshot = lease is null ? null : new ArtifactContentSnapshot(lease.Content.ToArray(), lease.Revision);
        try { await scope.FinishAsync(ct); return snapshot; }
        catch { if (snapshot is not null) System.Security.Cryptography.CryptographicOperations.ZeroMemory(snapshot.Content); throw; }
    }
    public Task<ArtifactContentMutationReceipt?> GetMutationOutcomeAsync(ArtifactContentOperationId id, CancellationToken ct = default)
        => Read(() => inner.GetMutationOutcomeAsync(id, ct), ct);
    public Task<IReadOnlyList<ArtifactContentAuditObligation>> ReadAuditObligationsAsync(ArtifactContentReceiptCursor? after,
        int limit, CancellationToken ct = default)
        => throw new NotSupportedException("ZIP child delegation cannot enumerate physical audit receipts.");
    public Task<ArtifactContentRevision?> ReadCurrentRevisionAsync(ArtifactId id, CancellationToken ct = default)
        => Read(() => inner.ReadCurrentRevisionAsync(id, ct), ct);
    public Task<IArtifactContentRevisionProbe> PrepareRevisionValidationAsync(ArtifactId id, CancellationToken ct = default)
        => Read(async () => (IArtifactContentRevisionProbe)new Probe(await inner.PrepareRevisionValidationAsync(id, ct), gate), ct);
    public async Task<IPreparedArtifactContentMutation> PreparePhysicalCreateAsync(ArtifactId id,
        ReadOnlyMemory<byte> content, ArtifactContentMutationContext context, CancellationToken ct = default)
    {
        var scope = await gate.OpenAsync(ZipChildIngestionEffect.PhysicalPrepare, ct);
        var combined = new MutableGuard(scope);
        try
        {
            var prepared = await Guarded.PreparePhysicalCreateGuardedAsync(id, content, context, combined, ct);
            return new Prepared(prepared, scope, combined, gate);
        }
        catch { await scope.DisposeAsync(); throw; }
    }
    public Task<IPreparedArtifactContentMutation> PreparePhysicalDeleteAsync(ArtifactId id, ArtifactContentRevision expected,
        ArtifactContentMutationContext context, CancellationToken ct = default)
        => throw new NotSupportedException("ZIP child delegation does not perform generic cleanup deletion.");
    public Task<ArtifactContentMutationResult> CreateIfAbsentAsync(ArtifactId id, ReadOnlyMemory<byte> content,
        ArtifactContentMutationContext context, CancellationToken ct = default)
        => throw new NotSupportedException("ZIP child creation requires guarded preparation.");
    public Task<ArtifactContentMutationResult> ReplaceIfRevisionMatchesAsync(ArtifactId id, ArtifactContentRevision expected,
        ReadOnlyMemory<byte> content, ArtifactContentMutationContext context, CancellationToken ct = default)
        => throw new NotSupportedException("ZIP child delegation cannot replace content.");
    public Task<ArtifactContentMutationResult> DeleteIfRevisionMatchesAsync(ArtifactId id, ArtifactContentRevision expected,
        ArtifactContentMutationContext context, CancellationToken ct = default)
        => throw new NotSupportedException("ZIP child delegation cannot delete content.");
    public Task WriteAsync(ArtifactId id, ReadOnlyMemory<byte> content, CancellationToken ct = default)
        => throw new NotSupportedException("ZIP child delegation cannot use legacy writes.");
    public Task<byte[]?> ReadAsync(ArtifactId id, CancellationToken ct = default)
        => throw new NotSupportedException("ZIP child delegation cannot use unbounded reads.");
    public Task DeleteAsync(ArtifactId id, CancellationToken ct = default)
        => throw new NotSupportedException("ZIP child delegation cannot use legacy deletes.");

    private sealed class Probe(IArtifactContentRevisionProbe inner, IZipChildIngestionEffectGate gate) : IArtifactContentRevisionProbe
    {
        public Task<ArtifactContentRevision?> ReadCurrentRevisionAsync(CancellationToken ct = default)
            => ZipGuardedChildEffects.RunAsync(gate, ZipChildIngestionEffect.PhysicalRead,
                async scope => { Task<ArtifactContentRevision?> pending; using (scope.Accept(ct)) pending = inner.ReadCurrentRevisionAsync(ct); return await pending; }, ct);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    private sealed class Prepared(IPreparedArtifactContentMutation inner, IZipChildIngestionEffectScope preparation,
        MutableGuard combined, IZipChildIngestionEffectGate gate) : IPreparedArtifactContentMutation
    {
        private bool disposed;
        public async Task<ArtifactContentMutationResult> ExecuteAsync(CancellationToken ct = default)
        {
            await using var creation = await gate.OpenAsync(ZipChildIngestionEffect.PhysicalCreate, ct);
            combined.Add(creation);
            var result = await inner.ExecuteAsync(ct);
            await creation.FinishAsync(ct);
            return result;
        }
        public async ValueTask DisposeAsync()
        {
            if (disposed) return;
            disposed = true;
            try { await inner.DisposeAsync(); }
            finally { await preparation.DisposeAsync(); }
        }
    }
    private sealed class MutableGuard(IZipEffectBoundaryGuard primary) : IZipEffectBoundaryGuard
    {
        private IZipEffectBoundaryGuard? secondary;
        public CancellationToken Invalidated => primary.Invalidated;
        internal void Add(IZipEffectBoundaryGuard next)
        {
            if (Interlocked.CompareExchange(ref secondary, next, null) is not null)
                throw new InvalidOperationException("Physical creation scope already installed.");
        }
        public async ValueTask RevalidateAsync(CancellationToken ct = default)
        {
            await primary.RevalidateAsync(ct);
            if (secondary is { } current) await current.RevalidateAsync(ct);
        }
        public IDisposable Accept(CancellationToken ct = default)
        {
            var first = primary.Accept(ct);
            try
            {
                var second = secondary?.Accept(ct);
                return new Tickets(first, second);
            }
            catch { first.Dispose(); throw; }
        }
        private sealed class Tickets(IDisposable first, IDisposable? second) : IDisposable
        {
            public void Dispose() { try { second?.Dispose(); } finally { first.Dispose(); } }
        }
    }
}

internal sealed class ZipGuardedIngestionAudit(IAcknowledgedSecurityAuditSink inner,
    IZipChildIngestionEffectGate gate) : IAcknowledgedSecurityAuditSink
{
    public Task<SecurityAuditAcknowledgement> AppendAsync(SecurityAuditRecord record, CancellationToken ct = default)
    {
        if (inner is not IZipGuardedAcknowledgedSecurityAuditSink guarded)
            throw new NotSupportedException("Guarded child audit sink is required.");
        return ZipGuardedChildEffects.RunAsync(gate, ZipChildIngestionEffect.AuditWrite,
            scope => guarded.AppendGuardedAsync(record, scope, ct), ct);
    }
    public Task<VerifiedSecurityAuditEvent?> FindVerifiedAsync(SecurityAuditEventId id, CancellationToken ct = default)
        => ZipGuardedChildEffects.RunAsync(gate, ZipChildIngestionEffect.AuditRead,
            async scope => { Task<VerifiedSecurityAuditEvent?> pending; using (scope.Accept(ct)) pending = inner.FindVerifiedAsync(id, ct); return await pending; }, ct);
    public Task WriteAsync(SecurityAuditRecord record, CancellationToken ct = default)
        => throw new NotSupportedException("ZIP child audit requires canonical acknowledged append.");
}
