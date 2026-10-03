using System.Security.Cryptography;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Persistence.Storage;
using EMF.Security.Auditing;
using EMF.Security.Auditing.Models;
using EMF.Security.Authorization;
using EMF.Security.Encryption;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Models;
using EMF.Security.Encryption.Envelope.Services;
using EMF.Security.Encryption.Models;
using EMF.Security.Models;
using EMF.Security.Storage;
using EMF.Security.Storage.Models;
using EMF.Security.Persistence.Sqlite;
using EMF.Security.Persistence.Sqlite.Auditing;
using Microsoft.Data.Sqlite;
namespace EMF.Tests;

public sealed class ArtifactEnvelopeRewrappingDurabilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_replacement_or_deletion_defeats_stale_rewrap(bool delete)
    {
        await using var f = await Fixture.CreateAsync();
        byte[]? newer = null;
        var provider = new HookProvider(f.Provider, async () =>
        {
            if (delete) await f.Physical.DeleteAsync(f.Id);
            else { await f.Physical.WriteAsync(f.Id, new byte[] { 8, 9, 10 }); newer = await f.Physical.ReadAsync(f.Id); }
        });
        var result = await f.Service(provider: provider).RewrapAsync(f.Request);
        Assert.Equal(delete ? ArtifactEnvelopeRewrappingOutcome.NotFound : ArtifactEnvelopeRewrappingOutcome.VersionConflict, result.Outcome);
        Assert.Equal(newer, await f.Physical.ReadAsync(f.Id));
        var receipt = await f.Physical.GetMutationOutcomeAsync(new(f.Request.OperationId.Value));
        Assert.NotNull(receipt); Assert.Equal(result.CompletedUtc, receipt.OccurredUtc);
        Assert.Equal(result.OperationId.Value, receipt.OperationId.Value);
    }
    [Fact]
    public async Task Lost_response_reconciles_original_receipt_and_restart_does_not_mutate_again()
    {
        await using var f = await Fixture.CreateAsync();
        var store = new ResponseLossStore(f.Physical);
        var first = await f.Service(store: store).RewrapAsync(f.Request);
        Assert.Equal(ArtifactEnvelopeRewrappingOutcome.Updated, first.Outcome);
        var snapshot = await f.Physical.ReadVersionedAsync(f.Id);
        var second = await f.Restart(store).RewrapAsync(f.Request);
        Assert.Equal(first.CompletedUtc, second.CompletedUtc); Assert.Equal(first.OperationId, second.OperationId);
        Assert.Equal(snapshot!.Revision, (await f.Physical.ReadVersionedAsync(f.Id))!.Revision);
        Assert.Equal(1, store.Replacements);
        Assert.Equal(1, (await f.Physical.ReadAuditObligationsAsync(null, 100)).Count);
        Assert.Equal(1, (await new SqliteSecurityAuditIntegrityVerifier(f.AuditPath).VerifyAsync()).ProtectedRecordCount);
    }
    [Fact]
    public async Task Restart_resumes_the_exact_prepared_candidate_without_rewrapping()
    {
        await using var f = await Fixture.CreateAsync();
        var journal = new FaultJournal(f.Journal) { StopAfterPrepared = true };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Service(journal: journal).RewrapAsync(f.Request));
        var staged = await f.Staging.ReadAsync(new(f.Request.OperationId.Value));
        Assert.NotNull(staged);
        Assert.Null(await f.Physical.GetMutationOutcomeAsync(new(f.Request.OperationId.Value)));
        var provider = new NeverRewrapProvider();
        var result = await f.Restart(provider: provider).RewrapAsync(f.Request);
        Assert.Equal(ArtifactEnvelopeRewrappingOutcome.Updated, result.Outcome);
        Assert.Equal(staged, await f.Physical.ReadAsync(f.Id));
        Assert.Equal(0, provider.Calls);
    }
    [Fact]
    public async Task Audit_outage_after_commit_returns_updated_with_visible_pending_work()
    {
        await using var f = await Fixture.CreateAsync();
        var result = await f.Service(audit: new AuditFault(f.Audit)).RewrapAsync(f.Request);
        Assert.Equal(ArtifactEnvelopeRewrappingOutcome.Updated, result.Outcome);
        Assert.Equal(ArtifactAuditDeliveryState.Pending, result.AuditDelivery);
        var pending = Assert.Single(await f.Journal.ReadPendingAsync(100));
        Assert.Equal(ArtifactRewrapState.CommittedAuditPending, pending.State);
        Assert.NotNull(pending.Event); Assert.Equal(result.CompletedUtc, pending.Event.OccurredUtc);
        Assert.Equal(f.Request.SubjectId, pending.Event.OriginalActorId);
        var revision = (await f.Physical.ReadVersionedAsync(f.Id))!.Revision;
        var recovered = await f.Restart().RewrapAsync(f.Request);
        Assert.Equal(ArtifactAuditDeliveryState.Completed, recovered.AuditDelivery);
        Assert.Equal(revision, (await f.Physical.ReadVersionedAsync(f.Id))!.Revision);
        Assert.Empty(await f.Journal.ReadPendingAsync(100));
    }
    [Fact]
    public async Task Journal_failure_after_mutation_keeps_receipt_and_committed_result()
    {
        await using var f = await Fixture.CreateAsync();
        var result = await f.Service(journal: new FaultJournal(f.Journal) { FailPending = true }).RewrapAsync(f.Request);
        Assert.Equal(ArtifactEnvelopeRewrappingOutcome.Updated, result.Outcome);
        Assert.Equal(ArtifactAuditDeliveryState.Pending, result.AuditDelivery);
        Assert.Equal(ArtifactRewrapState.Prepared, (await f.Journal.ReadAsync(f.Request.OperationId))!.State);
        var recovered = await f.Restart().RewrapAsync(f.Request);
        Assert.Equal(result.CompletedUtc, recovered.CompletedUtc);
        Assert.Equal(ArtifactAuditDeliveryState.Completed, recovered.AuditDelivery);
    }
    [Fact]
    public async Task Lost_audit_acknowledgement_recognizes_one_canonical_event_after_restart()
    {
        await using var f = await Fixture.CreateAsync();
        var first = await f.Service(audit: new AuditFault(f.Audit) { LoseAcknowledgement = true }).RewrapAsync(f.Request);
        Assert.Equal(ArtifactAuditDeliveryState.Pending, first.AuditDelivery);
        var second = await f.Restart().RewrapAsync(f.Request);
        Assert.Equal(ArtifactAuditDeliveryState.Completed, second.AuditDelivery);
        Assert.Equal(1, (await new SqliteSecurityAuditIntegrityVerifier(f.AuditPath).VerifyAsync()).ProtectedRecordCount);
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Independent_receipts_detect_deleted_or_corrupt_journal_even_during_audit_outage(bool corrupt, bool outage)
    {
        await using var f = await Fixture.CreateAsync();
        await f.Service(audit: new AuditFault(f.Audit)).RewrapAsync(f.Request);
        using (var connection = new SqliteConnection($"Data Source={f.JournalPath};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = corrupt ? "UPDATE RewrapIntents SET Payload='{}'" : "DELETE FROM RewrapIntents"; command.ExecuteNonQuery();
        }
        await f.Service(audit: outage ? new AuditFault(f.Audit) : f.Audit).ReconcileAsync("recovery-service");
        var work = Assert.Single(await f.Journal.ReadRecoveryWorkAsync(100));
        Assert.Equal(f.Request.OperationId, work.OperationId); Assert.Equal(ArtifactRewrapState.RequiresReview, work.State);
        Assert.Equal(outage ? "AuditChainUnknown" : "MissingAuthorizedIntent", work.SafeFailureCategory);
        Assert.Single(await f.Physical.ReadAuditObligationsAsync(null, 100));
        Assert.Equal(outage ? 0 : 1, (await new SqliteSecurityAuditIntegrityVerifier(f.AuditPath).VerifyAsync()).ProtectedRecordCount);
        if (!outage)
        {
            var reviewEvent = (await f.Audit.FindVerifiedAsync(work.RecoveryEvent!.AuditEventId!.Value))!.Record;
            Assert.Equal("recovery-service", reviewEvent.RecoveryActorId);
            Assert.NotEqual(work.AuditEventId, reviewEvent.AuditEventId);
        }
    }
    [Fact]
    public async Task Provisional_or_unknown_classification_is_denied_before_provider_or_physical_mutation()
    {
        await using var f = await Fixture.CreateAsync();
        var lease = await f.Authority.AcquireAsync(f.Id); var classification = lease.ClassificationId; var revision = lease.ClassificationRevision;
        await lease.DisposeAsync();
        await f.Authority.SetAsync(f.Id, classification, false, revision);
        var provider = new NeverRewrapProvider(); var before = await f.Physical.ReadAsync(f.Id);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service(provider: provider).RewrapAsync(f.Request));
        Assert.Equal(0, provider.Calls); Assert.Equal(before, await f.Physical.ReadAsync(f.Id));
        Assert.Null(await f.Journal.ReadAsync(f.Request.OperationId));
    }
    [Theory]
    [InlineData("ciphertext")]
    [InlineData("nonce")]
    [InlineData("tag")]
    [InlineData("algorithm")]
    [InlineData("binding")]
    public async Task Authentication_failure_never_promotes_and_precedes_current_key_selection(string corruption)
    {
        await using var f = await Fixture.CreateAsync();
        var bytes = (await f.Physical.ReadAsync(f.Id))!;
        var envelope = System.Text.Json.JsonSerializer.Deserialize<EncryptedEnvelope>(bytes)!;
        if (corruption == "ciphertext") envelope.Ciphertext[0] ^= 1;
        if (corruption == "nonce") envelope.Nonce[0] ^= 1;
        if (corruption == "tag") envelope.AuthenticationTag[0] ^= 1;
        if (corruption == "algorithm") envelope = new()
        {
            FormatVersion = envelope.FormatVersion,
            Ciphertext = envelope.Ciphertext,
            Nonce = envelope.Nonce,
            AuthenticationTag = envelope.AuthenticationTag,
            WrappedDataEncryptionKey = envelope.WrappedDataEncryptionKey,
            KeyEncryptionKeyId = envelope.KeyEncryptionKeyId,
            Algorithm = "unapproved"
        };
        var provider = new DevelopmentEnvelopeKeyRewrappingService(f.Keys);
        var currentCalls = f.Keys.CurrentCalls;
        await Assert.ThrowsAnyAsync<CryptographicException>(() => provider.RewrapAuthenticatedAsync(envelope,
            ArtifactEnvelopeContext.Create(corruption == "binding" ? new("wrong-artifact") : f.Id)));
        Assert.Equal(currentCalls, f.Keys.CurrentCalls); Assert.Equal(bytes, await f.Physical.ReadAsync(f.Id));
    }
    [Fact]
    public async Task Already_current_is_authenticated_and_size_is_bounded_before_key_calls()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Service().RewrapAsync(f.Request);
        var envelope = System.Text.Json.JsonSerializer.Deserialize<EncryptedEnvelope>((await f.Physical.ReadAsync(f.Id))!)!;
        var current = f.Keys.CurrentCalls; envelope.AuthenticationTag[0] ^= 1;
        await Assert.ThrowsAnyAsync<CryptographicException>(() => f.Provider.RewrapAuthenticatedAsync(envelope, ArtifactEnvelopeContext.Create(f.Id)));
        Assert.Equal(current, f.Keys.CurrentCalls);
        var bounded = new DevelopmentEnvelopeKeyRewrappingService(f.Keys, 1);
        var historical = f.Keys.HistoricalCalls;
        await Assert.ThrowsAnyAsync<CryptographicException>(() => bounded.RewrapAuthenticatedAsync(envelope, ArtifactEnvelopeContext.Create(f.Id)));
        Assert.Equal(historical, f.Keys.HistoricalCalls);
    }
    [Fact]
    public async Task Changed_request_cannot_reuse_operation_identity()
    {
        await using var f = await Fixture.CreateAsync(); await f.Service().RewrapAsync(f.Request);
        var forged = new ArtifactEnvelopeRewrappingRequest { OperationId = f.Request.OperationId, ArtifactId = f.Id, SubjectId = "different-actor", ProtectionClassificationId = f.Request.ProtectionClassificationId };
        await Assert.ThrowsAsync<ArtifactContentIdempotencyException>(() => f.Service().RewrapAsync(forged));
        Assert.Single(await f.Physical.ReadAuditObligationsAsync(null, 100));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restart_of_prepared_rewrap_preserves_a_newer_object_or_tombstone(bool deleted)
    {
        await using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Service(journal: new FaultJournal(f.Journal) { StopAfterPrepared = true }).RewrapAsync(f.Request));
        if (deleted) await f.Physical.DeleteAsync(f.Id); else await f.Physical.WriteAsync(f.Id, new byte[] { 99 });
        var newer = await f.Physical.ReadAsync(f.Id);
        var result = await f.Restart(provider: new NeverRewrapProvider()).RewrapAsync(f.Request);
        Assert.Equal(deleted ? ArtifactEnvelopeRewrappingOutcome.NotFound : ArtifactEnvelopeRewrappingOutcome.VersionConflict, result.Outcome);
        Assert.Equal(newer, await f.Physical.ReadAsync(f.Id));
    }
    [Fact]
    public async Task Current_envelope_is_authenticated_without_a_second_physical_mutation()
    {
        await using var f = await Fixture.CreateAsync(); await f.Service().RewrapAsync(f.Request);
        var before = await f.Physical.ReadVersionedAsync(f.Id);
        var request = new ArtifactEnvelopeRewrappingRequest { ArtifactId = f.Id, SubjectId = f.Request.SubjectId, ProtectionClassificationId = f.Request.ProtectionClassificationId };
        var result = await f.Service().RewrapAsync(request);
        Assert.Equal(ArtifactEnvelopeRewrappingOutcome.AlreadyCurrent, result.Outcome);
        Assert.Equal(before!.Revision, (await f.Physical.ReadVersionedAsync(f.Id))!.Revision);
        Assert.Null(await f.Physical.GetMutationOutcomeAsync(new(request.OperationId.Value)));
        Assert.Equal(ArtifactRewrapState.Completed, (await f.Journal.ReadAsync(request.OperationId))!.State);
    }
    [Fact]
    public async Task Pending_audit_work_is_visible_to_out_of_band_health_monitoring()
    {
        await using var f = await Fixture.CreateAsync(); await f.Service(audit: new AuditFault(f.Audit)).RewrapAsync(f.Request);
        var alerts = new HealthAlerts();
        var monitor = new EMF.Security.Monitoring.ArtifactRewrapDeliveryHealthMonitor(f.Journal, alerts);
        var health = await monitor.ObserveAsync(DateTimeOffset.UtcNow.AddHours(1), TimeSpan.FromMinutes(5));
        Assert.Equal(1, health.PendingSampleCount); Assert.NotNull(health.OldestPendingUtc);
        var alert = Assert.Single(alerts.Records); Assert.Equal("SecurityMutationAuditDelivery", alert.AlertType);
        Assert.DoesNotContain("key-1", System.Text.Json.JsonSerializer.Serialize(alert));
    }
    private sealed class HealthAlerts : EMF.Security.Monitoring.ISecurityAlertSink
    {
        public List<EMF.Security.Monitoring.SecurityAlert> Records { get; } = [];
        public Task WriteAsync(EMF.Security.Monitoring.SecurityAlert record, CancellationToken ct = default) { Records.Add(record); return Task.CompletedTask; }
    }
    [Fact]
    public async Task Missing_intent_cannot_be_satisfied_by_a_mismatching_canonical_acknowledgement()
    {
        await using var f = await Fixture.CreateAsync(); await f.Service(audit: new AuditFault(f.Audit)).RewrapAsync(f.Request);
        var intent = (await f.Journal.ReadAsync(f.Request.OperationId))!;
        var contradictory = new SecurityAuditRecord
        {
            AuditEventId = intent.AuditEventId,
            OperationId = intent.OperationId,
            OriginalActorId = intent.OriginalActorId,
            SubjectId = intent.OriginalActorId,
            Operation = SecurityPermissions.ArtifactEnvelopeRewrap.ToString(),
            ResourceType = "Artifact",
            ResourceId = "wrong-artifact",
            PolicyDecision = AuthorizationDecision.Allow,
            Outcome = SecurityAuditOutcome.Succeeded,
            OccurredUtc = intent.Receipt!.OccurredUtc,
            Facts = new Dictionary<string, string> { { "disposition", "Updated" } }
        };
        await f.Audit.AppendAsync(contradictory);
        using (var c = new SqliteConnection($"Data Source={f.JournalPath};Pooling=False"))
        { c.Open(); using var command = c.CreateCommand(); command.CommandText = "DELETE FROM RewrapIntents"; command.ExecuteNonQuery(); }
        await f.Service().ReconcileAsync("recovery-service");
        Assert.Equal("CanonicalAcknowledgementConflict", (await f.Journal.ReadReviewAsync(f.Request.OperationId))!.SafeFailureCategory);
        Assert.Single(await f.Physical.ReadAuditObligationsAsync(null, 100));
    }
    [Fact]
    public async Task Recovery_delivery_preserves_original_actor_time_and_one_canonical_outcome()
    {
        await using var f = await Fixture.CreateAsync(); var committed = await f.Service(audit: new AuditFault(f.Audit)).RewrapAsync(f.Request);
        var eventId = (await f.Journal.ReadAsync(f.Request.OperationId))!.AuditEventId;
        await f.Service().ReconcileAsync("recovery-service"); await f.Service().ReconcileAsync("recovery-service");
        var record = (await f.Audit.FindVerifiedAsync(eventId))!.Record;
        Assert.Equal(f.Request.SubjectId, record.OriginalActorId); Assert.Equal(f.Request.SubjectId, record.SubjectId);
        Assert.Null(record.RecoveryActorId); Assert.Equal(committed.CompletedUtc, record.OccurredUtc);
        Assert.Equal(1, (await new SqliteSecurityAuditIntegrityVerifier(f.AuditPath).VerifyAsync()).ProtectedRecordCount);
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "emf-m4-" + Guid.NewGuid().ToString("N"));
        public ArtifactId Id { get; } = new("synthetic-rewrap-artifact");
        public string JournalPath => Path.Combine(Root, "journal", "rewrap.sqlite");
        public string AuthorityPath => Path.Combine(Root, "authority", "classification.sqlite");
        public string AuditPath => Path.Combine(Root, "audit.sqlite");
        public FileSystemArtifactContentStore Physical { get; private set; } = null!;
        public FileSystemArtifactContentStagingStore Staging { get; private set; } = null!;
        public SqliteArtifactRewrapJournal Journal { get; private set; } = null!;
        public SqliteArtifactMutationAuthority Authority { get; private set; } = null!;
        public SqliteSecurityAuditSink Audit { get; private set; } = null!;
        public Keys Keys { get; } = new();
        public DevelopmentEnvelopeKeyRewrappingService Provider => new(Keys);
        public ArtifactEnvelopeRewrappingRequest Request { get; private set; } = null!;
        public static async Task<Fixture> CreateAsync()
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux durability tests require Linux.");
            var f = new Fixture(); Directory.CreateDirectory(f.Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            f.Physical = new(Path.Combine(f.Root, "content")); f.Staging = new(Path.Combine(f.Root, "staging"));
            f.Journal = new(f.JournalPath); await f.Journal.InitializeAsync();
            f.Authority = new(f.AuthorityPath); await f.Authority.InitializeAsync();
            await f.Authority.SetAsync(f.Id, new(ProtectionClassifications.Confidential), true);
            f.Audit = new(f.AuditPath); await f.Audit.InitializeAsync();
            var encryption = new DevelopmentEnvelopeEncryptionService(f.Keys);
            var encrypted = new EncryptedArtifactContentStore(f.Physical, encryption); await encrypted.WriteAsync(f.Id, new byte[] { 1, 2, 3 });
            f.Keys.Current = "key-2"; f.Request = new() { ArtifactId = f.Id, SubjectId = "security-steward", ProtectionClassificationId = new(ProtectionClassifications.Confidential) };
            return f;
        }
        public ArtifactEnvelopeRewrappingService Service(IVersionedArtifactContentStore? store = null, IAuthenticatedEnvelopeKeyRewrappingService? provider = null,
            IAcknowledgedSecurityAuditSink? audit = null, IArtifactRewrapJournal? journal = null)
            => new(store ?? Physical, provider ?? Provider, new AllowPolicy(), audit ?? Audit, Authority, journal ?? Journal, Staging);
        public ArtifactEnvelopeRewrappingService Restart(IVersionedArtifactContentStore? store = null, IAuthenticatedEnvelopeKeyRewrappingService? provider = null)
            => new(store ?? new FileSystemArtifactContentStore(Path.Combine(Root, "content")), provider ?? Provider, new AllowPolicy(), new SqliteSecurityAuditSink(AuditPath),
                new SqliteArtifactMutationAuthority(AuthorityPath), new SqliteArtifactRewrapJournal(JournalPath), new FileSystemArtifactContentStagingStore(Path.Combine(Root, "staging")));
        public ValueTask DisposeAsync() { SqliteConnection.ClearAllPools(); Directory.Delete(Root, true); return ValueTask.CompletedTask; }
    }
    private sealed class AllowPolicy : IAuthorizationPolicy
    { public Task<AuthorizationDecision> EvaluateAsync(AuthorizationRequest request, CancellationToken cancellationToken = default) => Task.FromResult(AuthorizationDecision.Allow); }
    private sealed class Keys : IEncryptionKeyProvider
    {
        private readonly Dictionary<string, EncryptionKey> _keys = new()
        {
            ["key-1"] = new() { KeyId = "key-1", KeyMaterial = Enumerable.Repeat((byte)1, 32).ToArray() },
            ["key-2"] = new() { KeyId = "key-2", KeyMaterial = Enumerable.Repeat((byte)2, 32).ToArray() }
        };
        public string Current { get; set; } = "key-1";
        public int CurrentCalls { get; private set; }
        public int HistoricalCalls { get; private set; }
        public Task<string?> GetCurrentKeyIdAsync(CancellationToken cancellationToken = default) { CurrentCalls++; return Task.FromResult<string?>(Current); }
        public Task<EncryptionKey?> GetKeyAsync(string id, CancellationToken cancellationToken = default) { HistoricalCalls++; return Task.FromResult(_keys.GetValueOrDefault(id)); }
    }
    private sealed class HookProvider(IAuthenticatedEnvelopeKeyRewrappingService inner, Func<Task> after) : IAuthenticatedEnvelopeKeyRewrappingService
    {
        public async Task<EncryptedEnvelope> RewrapAuthenticatedAsync(EncryptedEnvelope envelope, ReadOnlyMemory<byte> context, CancellationToken cancellationToken = default)
        { var replacement = await inner.RewrapAuthenticatedAsync(envelope, context, cancellationToken); await after(); return replacement; }
    }
    private sealed class NeverRewrapProvider : IAuthenticatedEnvelopeKeyRewrappingService
    {
        public int Calls { get; private set; }
        public Task<EncryptedEnvelope> RewrapAuthenticatedAsync(EncryptedEnvelope envelope, ReadOnlyMemory<byte> context, CancellationToken cancellationToken = default)
        { Calls++; throw new InvalidOperationException("A prepared retry must not rewrap again."); }
    }
    private sealed class AuditFault(IAcknowledgedSecurityAuditSink inner) : IAcknowledgedSecurityAuditSink
    {
        public bool LoseAcknowledgement { get; init; }
        public Task WriteAsync(SecurityAuditRecord record, CancellationToken cancellationToken = default) => throw new IOException("Synthetic audit outage.");
        public async Task<SecurityAuditAcknowledgement> AppendAsync(SecurityAuditRecord record, CancellationToken cancellationToken = default)
        { if (LoseAcknowledgement) await inner.AppendAsync(record, cancellationToken); throw new IOException("Synthetic audit outage."); }
        public Task<VerifiedSecurityAuditEvent?> FindVerifiedAsync(SecurityAuditEventId id, CancellationToken cancellationToken = default) => throw new IOException("Synthetic audit outage.");
    }
    private sealed class FaultJournal(IArtifactRewrapJournal inner) : IArtifactRewrapJournal
    {
        public bool StopAfterPrepared { get; init; }
        public bool FailPending { get; init; }
        public Task<ArtifactRewrapIntent?> ReadAsync(SecurityMutationOperationId id, CancellationToken ct = default) => inner.ReadAsync(id, ct);
        public Task CreateAsync(ArtifactRewrapIntent intent, CancellationToken ct = default) => inner.CreateAsync(intent, ct);
        public async Task UpdateAsync(ArtifactRewrapIntent expected, ArtifactRewrapIntent updated, CancellationToken ct = default)
        {
            if (FailPending && updated.State == ArtifactRewrapState.CommittedAuditPending) throw new IOException("Synthetic journal outage.");
            await inner.UpdateAsync(expected, updated, ct); if (StopAfterPrepared && updated.State == ArtifactRewrapState.Prepared) throw new OperationCanceledException("Synthetic restart boundary.");
        }
        public Task<IReadOnlyList<ArtifactRewrapIntent>> ReadPendingAsync(int limit, CancellationToken ct = default) => inner.ReadPendingAsync(limit, ct);
        public Task<ArtifactRewrapRecoveryWork?> ReadReviewAsync(SecurityMutationOperationId id, CancellationToken ct = default) => inner.ReadReviewAsync(id, ct);
        public Task RecordReviewAsync(ArtifactRewrapRecoveryWork work, CancellationToken ct = default) => inner.RecordReviewAsync(work, ct);
        public Task<IReadOnlyList<ArtifactRewrapRecoveryWork>> ReadRecoveryWorkAsync(int limit, CancellationToken ct = default) => inner.ReadRecoveryWorkAsync(limit, ct);
    }
    private sealed class ResponseLossStore(IVersionedArtifactContentStore inner) : IVersionedArtifactContentStore
    {
        public int Replacements { get; private set; }
        public async Task<ArtifactContentMutationResult> ReplaceIfRevisionMatchesAsync(ArtifactId id, ArtifactContentRevision expected, ReadOnlyMemory<byte> content, ArtifactContentMutationContext context, CancellationToken ct = default)
        { Replacements++; await inner.ReplaceIfRevisionMatchesAsync(id, expected, content, context, ct); throw new IOException("Synthetic lost response."); }
        public Task<byte[]?> ReadAsync(ArtifactId id, CancellationToken ct = default) => inner.ReadAsync(id, ct);
        public Task WriteAsync(ArtifactId id, ReadOnlyMemory<byte> bytes, CancellationToken ct = default) => inner.WriteAsync(id, bytes, ct);
        public Task DeleteAsync(ArtifactId id, CancellationToken ct = default) => inner.DeleteAsync(id, ct);
        public Task<ArtifactContentSnapshot?> ReadVersionedAsync(ArtifactId id, CancellationToken ct = default) => inner.ReadVersionedAsync(id, ct);
        public Task<ArtifactContentMutationReceipt?> GetMutationOutcomeAsync(ArtifactContentOperationId id, CancellationToken ct = default) => inner.GetMutationOutcomeAsync(id, ct);
        public Task<ArtifactContentMutationResult> CreateIfAbsentAsync(ArtifactId id, ReadOnlyMemory<byte> content, ArtifactContentMutationContext context, CancellationToken ct = default) => inner.CreateIfAbsentAsync(id, content, context, ct);
        public Task<ArtifactContentMutationResult> DeleteIfRevisionMatchesAsync(ArtifactId id, ArtifactContentRevision expected, ArtifactContentMutationContext context, CancellationToken ct = default) => inner.DeleteIfRevisionMatchesAsync(id, expected, context, ct);
        public Task<IReadOnlyList<ArtifactContentAuditObligation>> ReadAuditObligationsAsync(ArtifactContentReceiptCursor? cursor, int limit, CancellationToken ct = default) => inner.ReadAuditObligationsAsync(cursor, limit, ct);
    }
}
