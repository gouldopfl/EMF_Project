using EMF.Integrity;
using System.Security.Cryptography;
using EMF.Core.Contracts.Ingestion;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Core.Models.Integrity;
using EMF.Orchestration.Services;
using EMF.Persistence.Repositories;
using EMF.Persistence.Storage;
using EMF.Security.Auditing;
using EMF.Security.Auditing.Models;
using EMF.Security.Authorization;
using EMF.Security.Encryption;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Models;
using EMF.Security.Encryption.Envelope.Services;
using EMF.Security.Encryption.Models;
using EMF.Security.Ingestion;
using EMF.Security.Models;
using EMF.Security.Models.Identities;
using EMF.Security.Persistence.Sqlite;
using EMF.Security.Persistence.Sqlite.Auditing;
using Microsoft.Data.Sqlite;

namespace EMF.Tests.TestInfrastructure;

internal sealed class ArtifactIngestionFixture : IAsyncDisposable
{
    public string Root { get; private set; } = Path.Combine(Path.GetTempPath(), "emf-m5-synthetic-" + Guid.NewGuid().ToString("N"));
    public string DatabasePath => Path.Combine(Root, "metadata.sqlite");
    public string AuditPath => Path.Combine(Root, "audit.sqlite");
    public string SourcePath => Path.Combine(Root, "synthetic.txt");
    public byte[] Content { get; } = "synthetic ingestion content"u8.ToArray();
    public ArtifactId Id { get; private set; } = new("synthetic-artifact-" + Guid.NewGuid().ToString("N"));
    public SqliteEvidenceRepository Repository { get; private set; } = null!;
    public SqliteArtifactIngestionPersistence Persistence { get; private set; } = null!;
    public FileSystemArtifactContentStore Physical { get; private set; } = null!;
    public FileSystemArtifactContentStagingStore Staging { get; private set; } = null!;
    public SqliteSecurityAuditSink Audit { get; private set; } = null!;
    public Context SecurityContext { get; } = new();
    public ClassificationPolicy Classification { get; } = new();
    public AuthorizationPolicy Authorization { get; } = new();
    public CountingEncryption Encryption { get; } = new(new DevelopmentEnvelopeEncryptionService(new Keys()));
    public Sha256ContentFingerprintService Fingerprints { get; } = new();
    public IngestionMetadataDraft Draft { get; private set; } = null!;
    public static async Task<ArtifactIngestionFixture> CreateAsync()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux ingestion durability tests require Linux.");
        var f = new ArtifactIngestionFixture();
        Directory.CreateDirectory(f.Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        await File.WriteAllBytesAsync(f.SourcePath, f.Content);
        f.Repository = new(f.DatabasePath); await f.Repository.InitializeAsync();
        File.SetUnixFileMode(f.DatabasePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        f.Persistence = new(f.DatabasePath); await f.Persistence.InitializeAsync();
        f.Physical = new(Path.Combine(f.Root, "content")); f.Staging = new(Path.Combine(f.Root, "staging"));
        f.Audit = new(f.AuditPath); await f.Audit.InitializeAsync();
        f.Draft = new(new Artifact { Id = f.Id, Name = "synthetic.txt", ArtifactType = "file", Fingerprint = await f.Fingerprints.ComputeAsync(f.Content) },
            new Provenance { ArtifactId = f.Id, Source = f.SourcePath, RecordedBy = "synthetic-discovery" });
        return f;
    }
    public static async Task<ArtifactIngestionFixture> OpenWorkerAsync(string root, ArtifactId id, AuthenticatedIngestionOperation operation)
    {
        var f = new ArtifactIngestionFixture { Root = root, Id = id };
        f.SecurityContext.Operation = operation;
        f.Repository = new(f.DatabasePath); f.Persistence = new(f.DatabasePath);
        f.Physical = new(Path.Combine(f.Root, "content")); f.Staging = new(Path.Combine(f.Root, "staging")); f.Audit = new(f.AuditPath);
        f.Draft = new(new Artifact { Id = id, Name = "synthetic.txt", ArtifactType = "file", Fingerprint = await f.Fingerprints.ComputeAsync(f.Content) },
            new Provenance { ArtifactId = id, Source = f.SourcePath, RecordedBy = "synthetic-discovery" });
        return f;
    }
    public ArtifactIngestionCoordinator Service(IArtifactIngestionPersistence? persistence = null,
        IVersionedArtifactContentStore? physical = null, IAcknowledgedSecurityAuditSink? audit = null, IArtifactContentStagingStore? staging = null)
        => new(persistence ?? Persistence, physical ?? Physical, staging ?? Staging, Encryption, SecurityContext, Classification, Authorization, audit ?? Audit, Fingerprints);
    public ArtifactIngestionCoordinator Restart(IVersionedArtifactContentStore? physical = null, IAcknowledgedSecurityAuditSink? audit = null)
        => new(new SqliteArtifactIngestionPersistence(DatabasePath), physical ?? new FileSystemArtifactContentStore(Path.Combine(Root, "content")),
            new FileSystemArtifactContentStagingStore(Path.Combine(Root, "staging")), Encryption, SecurityContext, Classification, Authorization,
            audit ?? new SqliteSecurityAuditSink(AuditPath), Fingerprints);
    public async Task<ArtifactIngestionIntent> IntentAsync()
    {
        await using var session = await Persistence.AcquireAsync(SecurityContext.Operation.OperationId);
        return session.Intent ?? throw new InvalidDataException();
    }
    public async Task SqlAsync(string sql, params (string, object)[] values)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString());
        await connection.OpenAsync(); using var command = connection.CreateCommand(); command.CommandText = sql;
        foreach (var (key, value) in values) command.Parameters.AddWithValue(key, value);
        await command.ExecuteNonQueryAsync();
    }
    public async Task ReclassifyAsync(ArtifactId id, string classification)
    {
        var authority = new SqliteArtifactMutationAuthority(DatabasePath);
        EMF.Security.Storage.ArtifactClassificationRevision revision; bool adopted;
        await using (var lease = await authority.AcquireAsync(id))
        { revision = lease.ClassificationRevision; adopted = lease.IsAdopted; }
        await authority.SetAsync(id, new(classification), adopted, revision);
    }
    public async Task StopAsync(string checkpoint, bool beforeCommit = false)
    {
        var fault = new FaultPersistence(Persistence, checkpoint, beforeCommit);
        if (checkpoint == "Completed" && !beforeCommit)
        {
            await Service(persistence: fault).IngestAsync(Draft, Content);
            Xunit.Assert.Equal(ArtifactIngestionState.Completed, (await IntentAsync()).State);
        }
        else await Xunit.Assert.ThrowsAsync<SimulatedCrashException>(() => Service(persistence: fault).IngestAsync(Draft, Content));
    }
    public ValueTask DisposeAsync()
    {
        // Release only this synthetic fixture's pooled connections. Clearing all
        // pools would interfere with unrelated tests running concurrently.
        foreach (var path in new[] { DatabasePath, AuditPath })
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            SqliteConnection.ClearPool(connection);
        }
        Directory.Delete(Root, true); return ValueTask.CompletedTask;
    }
    internal sealed class Keys : IEncryptionKeyProvider
    {
        public Task<string?> GetCurrentKeyIdAsync(CancellationToken ct = default) => Task.FromResult<string?>("synthetic-development-key");
        public Task<EncryptionKey?> GetKeyAsync(string id, CancellationToken ct = default) => Task.FromResult<EncryptionKey?>(new() { KeyId = id, KeyMaterial = Enumerable.Repeat((byte)37, 32).ToArray() });
    }
    internal sealed class Context : IArtifactIngestionSecurityContext
    {
        public AuthenticatedIngestionOperation Operation { get; set; } = new(ArtifactContentOperationId.New(), new(Guid.NewGuid().ToString("N")), "synthetic-ingestion-actor");
        public string RecoveryActor { get; set; } = "synthetic-recovery-service";
        public bool AllowReview { get; set; } = true;
        public List<(string Actor, ArtifactContentOperationId Operation, ArtifactId? Artifact)> Reviews { get; } = [];
        public Task<bool> AuthorizeNonDestructiveReviewAsync(string actor, ArtifactContentOperationId operation, ArtifactId? artifact,
            CancellationToken ct = default)
        { Reviews.Add((actor, operation, artifact)); return Task.FromResult(AllowReview); }
        public Task<AuthenticatedIngestionOperation> GetIngestionOperationAsync(CancellationToken ct = default) => Task.FromResult(Operation);
        public Task<string> GetRecoveryActorAsync(CancellationToken ct = default) => Task.FromResult(RecoveryActor);
    }
    internal sealed class ClassificationPolicy : IArtifactIngestionClassificationPolicy
    {
        public string? Classification { get; set; } = "Confidential";
        public bool AdoptionAllowed { get; set; } = true;
        public bool CanonicalAgreement { get; set; } = true;
        public Task<ProtectionClassificationId?> ResolveProvisionalAsync(AuthenticatedIngestionOperation operation, CancellationToken ct = default)
            => Task.FromResult(Classification is null ? (ProtectionClassificationId?)null : new(Classification));
        public Task<bool> CanAdoptAsync(IngestionClassificationAuthority authority, Artifact proposed, CancellationToken ct = default)
            => Task.FromResult(AdoptionAllowed && (!proposed.Metadata.TryGetValue("protectionClassificationId", out var value)
                || string.Equals(value.ToString(), authority.ClassificationId.Value, StringComparison.Ordinal)));
        public Task<bool> CanonicalClassificationAgreesAsync(IngestionClassificationAuthority provisional, IngestionClassificationAuthority canonical, CancellationToken ct = default)
            => Task.FromResult(CanonicalAgreement && provisional.ClassificationId == canonical.ClassificationId);
    }
    internal sealed class AuthorizationPolicy : IAuthorizationPolicy
    {
        public bool AllowIngestion { get; set; } = true;
        public bool AllowRecovery { get; set; } = true;
        public Func<AuthorizationRequest, Task>? Hook { get; set; }
        public List<AuthorizationRequest> Requests { get; } = [];
        public async Task<AuthorizationDecision> EvaluateAsync(AuthorizationRequest request, CancellationToken ct = default)
        {
            Requests.Add(request); if (Hook is not null) await Hook(request);
            return (request.PermissionId == SecurityPermissions.ArtifactIngestionRecover ? AllowRecovery : AllowIngestion)
                ? AuthorizationDecision.Allow : AuthorizationDecision.Deny;
        }
    }
    internal sealed class CountingEncryption(IEnvelopeEncryptionService inner) : IEnvelopeEncryptionService
    {
        public int Encryptions { get; private set; }
        public Task<EncryptedEnvelope> EncryptWithContextAsync(ReadOnlyMemory<byte> content, ReadOnlyMemory<byte> context, CancellationToken ct = default)
        { Encryptions++; return inner.EncryptWithContextAsync(content, context, ct); }
        public Task<byte[]> DecryptWithContextAsync(EncryptedEnvelope envelope, ReadOnlyMemory<byte> context, CancellationToken ct = default)
            => inner.DecryptWithContextAsync(envelope, context, ct);
        public Task<EncryptedEnvelope> EncryptAsync(ReadOnlyMemory<byte> content, CancellationToken ct = default) => inner.EncryptAsync(content, ct);
        public Task<byte[]> DecryptAsync(EncryptedEnvelope envelope, CancellationToken ct = default) => inner.DecryptAsync(envelope, ct);
    }
    internal sealed class AuditOutage : IAcknowledgedSecurityAuditSink
    {
        public Task WriteAsync(SecurityAuditRecord record, CancellationToken ct = default) => throw new IOException("synthetic audit outage");
        public Task<SecurityAuditAcknowledgement> AppendAsync(SecurityAuditRecord record, CancellationToken ct = default) => throw new IOException("synthetic audit outage");
        public Task<VerifiedSecurityAuditEvent?> FindVerifiedAsync(SecurityAuditEventId id, CancellationToken ct = default) => throw new IOException("synthetic audit outage");
    }
    internal sealed class SimulatedCrashException : OperationCanceledException;
    internal sealed class FaultPersistence(IArtifactIngestionPersistence inner, string checkpoint, bool beforeCommit = false) : IArtifactIngestionPersistence
    {
        public Func<Task>? OnFault { get; init; }
        public bool ContinueAfterCheckpoint { get; init; }
        private bool _fired;
        private string Checkpoint => checkpoint;
        private bool BeforeCommit => beforeCommit;
        public async Task<IArtifactIngestionSession> AcquireAsync(ArtifactContentOperationId id, CancellationToken ct = default)
            => new FaultSession(await inner.AcquireAsync(id, ct), this);
        public Task<IReadOnlyList<IngestionRecoveryWork>> ReadRecoveryWorkAsync(long after, int limit, CancellationToken ct = default) => inner.ReadRecoveryWorkAsync(after, limit, ct);
        public Task<IReadOnlyList<IngestionAuditObligation>> ReadAuditObligationsAsync(ArtifactContentOperationId id, CancellationToken ct = default) => inner.ReadAuditObligationsAsync(id, ct);
        public Task<IngestionAuditObligation?> ReadReceiptAuditObligationAsync(ArtifactContentMutationReceipt receipt, CancellationToken ct = default) => inner.ReadReceiptAuditObligationAsync(receipt, ct);
        public Task<IngestionAuditObligation?> ReadReviewAuditObligationAsync(ArtifactContentOperationId id, CancellationToken ct = default) => inner.ReadReviewAuditObligationAsync(id, ct);
        public Task AcknowledgeAuditAsync(IngestionAuditObligation obligation, CancellationToken ct = default) => inner.AcknowledgeAuditAsync(obligation, ct);
        public Task MarkAuditPendingAsync(IngestionAuditObligation obligation, CancellationToken ct = default) => inner.MarkAuditPendingAsync(obligation, ct);
        public Task RecordOrphanReceiptAsync(ArtifactContentMutationReceipt receipt, CancellationToken ct = default) => inner.RecordOrphanReceiptAsync(receipt, ct);
        public Task<ArtifactContentOperationId?> FindReceiptOperationAsync(ArtifactContentMutationReceipt receipt, CancellationToken ct = default) => inner.FindReceiptOperationAsync(receipt, ct);
        public Task<IngestionRecoveryHealth> ReadRecoveryHealthAsync(CancellationToken ct = default) => inner.ReadRecoveryHealthAsync(ct);
        private sealed class FaultSession(IArtifactIngestionSession innerSession, FaultPersistence owner) : IArtifactIngestionSession
        {
            public ArtifactContentOperationId OperationId => innerSession.OperationId;
            public ArtifactIngestionIntent? Intent => innerSession.Intent;
            public IngestionOperationBinding? OperationBinding => innerSession.OperationBinding;
            public ArtifactId? ProvisionalArtifactId => innerSession.ProvisionalArtifactId;
            public bool IsDamaged => innerSession.IsDamaged;
            public bool HasReview => innerSession.HasReview;
            public Task<bool> HasAdoptionEvidenceAsync(ArtifactId id, CancellationToken ct = default) => innerSession.HasAdoptionEvidenceAsync(id, ct);
            public Task<IngestionClassificationAuthority?> ResolveAuthorityAsync(ArtifactId id, CancellationToken ct = default) => innerSession.ResolveAuthorityAsync(id, ct);
            public Task<IngestionMetadataDraft?> ReadDraftAsync(CancellationToken ct = default) => innerSession.ReadDraftAsync(ct);
            public Task<IngestionMetadataDraft?> FindCanonicalAsync(CancellationToken ct = default) => innerSession.FindCanonicalAsync(ct);
            public Task<IngestionMetadataDraft?> ReadResultAsync(CancellationToken ct = default) => innerSession.ReadResultAsync(ct);
            public Task PrepareAsync(ArtifactIngestionIntent intent, IngestionOperationBinding binding, IngestionMetadataDraft draft, CancellationToken ct = default) => innerSession.PrepareAsync(intent, binding, draft, ct);
            public Task SetCandidateAsync(ArtifactIngestionIntent intent, string hash, CancellationToken ct = default) => innerSession.SetCandidateAsync(intent, hash, ct);
            public Task RecordCreatedAsync(ArtifactIngestionIntent intent, ArtifactContentMutationReceipt receipt, CancellationToken ct = default) => innerSession.RecordCreatedAsync(intent, receipt, ct);
            public Task AdoptAsync(ArtifactIngestionIntent intent, IngestionClassificationAuthority provisional, IngestionClassificationAuthority? canonical, string? actor, CancellationToken ct = default) => innerSession.AdoptAsync(intent, provisional, canonical, actor, ct);
            public Task ClaimCleanupAsync(ArtifactIngestionIntent intent, IngestionClassificationAuthority authority, string actor, CancellationToken ct = default) => innerSession.ClaimCleanupAsync(intent, authority, actor, ct);
            public Task RecordDeduplicationConflictAsync(ArtifactIngestionIntent intent, IngestionClassificationAuthority authority, ArtifactId canonical, string? actor, CancellationToken ct = default) => innerSession.RecordDeduplicationConflictAsync(intent, authority, canonical, actor, ct);
            public Task RecordCleanedAsync(ArtifactIngestionIntent intent, ArtifactContentMutationReceipt? receipt, string actor, CancellationToken ct = default) => innerSession.RecordCleanedAsync(intent, receipt, actor, ct);
            public Task RecordReviewAsync(string category, string actor, CancellationToken ct = default, bool isRecovery = true) => innerSession.RecordReviewAsync(category, actor, ct, isRecovery);
            public Task EnsureReceiptAuditAsync(ArtifactContentMutationReceipt receipt, CancellationToken ct = default) => innerSession.EnsureReceiptAuditAsync(receipt, ct);
            public Task EnsureAdoptionAuditAsync(CancellationToken ct = default) => innerSession.EnsureAdoptionAuditAsync(ct);
            public Task RecordRecoveryCompletionAsync(string actor, CancellationToken ct = default) => innerSession.RecordRecoveryCompletionAsync(actor, ct);
            public Task CompleteAsync(ArtifactIngestionIntent intent, CancellationToken ct = default) => innerSession.CompleteAsync(intent, ct);
            public Task ReopenAuditDeliveryAsync(ArtifactIngestionIntent intent, CancellationToken ct = default) => innerSession.ReopenAuditDeliveryAsync(intent, ct);
            public async Task CommitAsync(CancellationToken ct = default)
            {
                var state = Intent is { State: ArtifactIngestionState.Prepared, CandidateHash: not null } ? "Candidate" : Intent?.State.ToString();
                var fire = !owner._fired && state == owner.Checkpoint;
                if (fire && owner.BeforeCommit)
                { owner._fired = true; if (owner.OnFault is not null) await owner.OnFault(); if (!owner.ContinueAfterCheckpoint) throw new SimulatedCrashException(); }
                await innerSession.CommitAsync(ct);
                if (fire)
                { owner._fired = true; if (owner.OnFault is not null) await owner.OnFault(); if (!owner.ContinueAfterCheckpoint) throw new SimulatedCrashException(); }
            }
            public ValueTask DisposeAsync() => innerSession.DisposeAsync();
        }
    }
    internal sealed class FaultStore(IVersionedArtifactContentStore inner) : IVersionedArtifactContentStore
    {
        public bool LoseCreateResponse { get; set; }
        public bool LoseDeleteResponse { get; set; }
        public bool CancelAfterCreate { get; set; }
        public bool HideCreateReceipt { get; set; }
        public bool FailDelete { get; set; }
        public int Creates { get; private set; }
        public int Deletes { get; private set; }
        public Func<Task>? BeforeDelete { get; set; }
        public Func<Task>? AfterCreate { get; set; }
        public Func<Task>? AfterDelete { get; set; }
        public async Task<ArtifactContentMutationResult> CreateIfAbsentAsync(ArtifactId id, ReadOnlyMemory<byte> content, ArtifactContentMutationContext context, CancellationToken ct = default)
        {
            Creates++; var result = await inner.CreateIfAbsentAsync(id, content, context, ct);
            if (AfterCreate is not null) await AfterCreate();
            if (CancelAfterCreate) throw new SimulatedCrashException();
            if (LoseCreateResponse) throw new IOException("synthetic lost create response"); return result;
        }
        public async Task<ArtifactContentMutationResult> DeleteIfRevisionMatchesAsync(ArtifactId id, ArtifactContentRevision revision, ArtifactContentMutationContext context, CancellationToken ct = default)
        {
            Deletes++; if (BeforeDelete is not null) await BeforeDelete(); if (FailDelete) throw new IOException("synthetic cleanup failure");
            var result = await inner.DeleteIfRevisionMatchesAsync(id, revision, context, ct);
            if (AfterDelete is not null) await AfterDelete();
            if (LoseDeleteResponse) throw new IOException("synthetic lost delete response"); return result;
        }
        public Task<ArtifactContentMutationReceipt?> GetMutationOutcomeAsync(ArtifactContentOperationId id, CancellationToken ct = default)
            => HideCreateReceipt ? Task.FromResult<ArtifactContentMutationReceipt?>(null) : inner.GetMutationOutcomeAsync(id, ct);
        public Task<IReadOnlyList<ArtifactContentAuditObligation>> ReadAuditObligationsAsync(ArtifactContentReceiptCursor? after, int limit, CancellationToken ct = default) => inner.ReadAuditObligationsAsync(after, limit, ct);
        public Task<ArtifactContentSnapshot?> ReadVersionedAsync(ArtifactId id, CancellationToken ct = default) => inner.ReadVersionedAsync(id, ct);
        public Task<ArtifactContentMutationResult> ReplaceIfRevisionMatchesAsync(ArtifactId id, ArtifactContentRevision revision, ReadOnlyMemory<byte> content, ArtifactContentMutationContext context, CancellationToken ct = default) => inner.ReplaceIfRevisionMatchesAsync(id, revision, content, context, ct);
        public Task WriteAsync(ArtifactId id, ReadOnlyMemory<byte> content, CancellationToken ct = default) => throw new InvalidOperationException("Unconditional ingestion write is forbidden.");
        public Task DeleteAsync(ArtifactId id, CancellationToken ct = default) => throw new InvalidOperationException("Unconditional ingestion delete is forbidden.");
        public Task<byte[]?> ReadAsync(ArtifactId id, CancellationToken ct = default) => inner.ReadAsync(id, ct);
    }
}
