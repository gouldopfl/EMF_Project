using EMF.Core.Contracts.Ingestion;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Services;
using EMF.Security.Auditing;
using EMF.Security.Auditing.Models;
using EMF.Tests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using static EMF.Tests.TestInfrastructure.ArtifactIngestionFixture;

namespace EMF.Tests;

public sealed class ArtifactIngestionReconciliationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Reconciliation_authorizes_before_any_durable_write_and_review_never_grants_repair(bool directFinish, bool allowReview)
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        await f.Service(audit: new AuditOutage()).IngestAsync(f.Draft, f.Content);
        var intent = await f.IntentAsync();
        var snapshot = await DatabaseSnapshotAsync(f.DatabasePath);
        f.Authorization.AllowRecovery = false; f.SecurityContext.AllowReview = allowReview;
        f.Authorization.Requests.Clear();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var decisions = 0;
        f.Authorization.Hook = async request =>
        {
            Assert.Equal(EMF.Security.Models.SecurityPermissions.ArtifactIngestionRecover, request.PermissionId);
            Assert.Equal(f.SecurityContext.RecoveryActor, request.SubjectId);
            Assert.Equal(f.Id.Value, request.ResourceId);
            Assert.Equal("Confidential", request.ProtectionClassificationId!.Value);
            if (++decisions != 1) return;
            Assert.Equal(snapshot, await DatabaseSnapshotAsync(f.DatabasePath));
            entered.SetResult(); await release.Task;
        };
        var service = f.Restart();
        var work = Task.Run(async () =>
        {
            if (directFinish) await service.FinishAsync(intent.OperationId);
            else await service.ReconcileReceiptsAsync(null, 100);
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // The authorization decision is still unresolved. No intent, fact,
            // review, outbox, adoption, classification or metadata write is allowed.
            Assert.Equal(snapshot, await DatabaseSnapshotAsync(f.DatabasePath));
            Assert.Null(await f.Audit.FindVerifiedAsync(new(intent.CreateEventId.Value)));
        }
        finally { release.TrySetResult(); }
        if (allowReview)
        {
            await work;
            Assert.Equal(ArtifactIngestionState.RequiresReview, (await f.IntentAsync()).State);
            Assert.NotNull(await f.Persistence.ReadReviewAuditObligationAsync(intent.OperationId));
        }
        else
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => work);
            Assert.Equal(snapshot, await DatabaseSnapshotAsync(f.DatabasePath));
            Assert.Equal(ArtifactIngestionState.MetadataCommitted, (await f.IntentAsync()).State);
        }
        Assert.True(decisions > 0);
        Assert.Null(await f.Audit.FindVerifiedAsync(new(intent.CreateEventId.Value)));
        Assert.Null(await f.Physical.GetMutationOutcomeAsync(intent.CleanupOperationId));
        Assert.NotNull(await f.Physical.ReadAsync(f.Id));
        Assert.NotEmpty(await f.Physical.ReadAuditObligationsAsync(null, 100));
    }
    private static async Task<string> DatabaseSnapshotAsync(string path)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        await connection.OpenAsync();
        var tables = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        }
        var rows = new List<object>();
        foreach (var table in tables)
        {
            using var command = connection.CreateCommand(); command.CommandText = "SELECT * FROM \"" + table.Replace("\"", "\"\"") + "\" ORDER BY rowid";
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) rows.Add(new { table, values = Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? null : reader.GetValue(i)).ToArray() });
        }
        return System.Text.Json.JsonSerializer.Serialize(rows);
    }
    [Theory]
    [InlineData(false, "missing")]
    [InlineData(true, "missing")]
    [InlineData(false, "conflicting")]
    [InlineData(true, "conflicting")]
    [InlineData(false, "matching")]
    [InlineData(true, "matching")]
    [InlineData(false, "payload")]
    [InlineData(true, "payload")]
    [InlineData(false, "unavailable")]
    [InlineData(true, "unavailable")]
    public async Task Acknowledged_review_never_substitutes_for_original_receipt_acknowledgement(bool corrupt, string originalStatus)
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        await f.Service(audit: new AuditOutage()).IngestAsync(f.Draft, f.Content);
        var intent = await f.IntentAsync();
        var original = Assert.Single((await f.Persistence.ReadAuditObligationsAsync(intent.OperationId)).Where(x => x.Action == IngestionAuditAction.Created));
        await f.SqlAsync(corrupt ? "UPDATE ArtifactIngestionIntents SET IntentJson='broken'" : "DELETE FROM ArtifactIngestionIntents");
        await f.Restart().RecoverOperationAsync(intent.OperationId);
        var review = (await f.Persistence.ReadReviewAuditObligationAsync(intent.OperationId))!;
        Assert.NotNull(await f.Audit.FindVerifiedAsync(new(review.EventId.Value)));
        if (originalStatus != "missing")
        {
            await f.Audit.AppendAsync(new()
            {
                AuditEventId = new(original.EventId.Value), OperationId = new(original.OperationId.Value),
                ResourceType = EMF.Security.Models.SecurityResourceTypes.Artifact,
                ResourceId = originalStatus == "conflicting" ? original.ArtifactId.Value + "-conflict" : original.ArtifactId.Value,
                Operation = EMF.Security.Models.SecurityPermissions.ArtifactIngest.Value,
                OriginalActorId = originalStatus == "payload" ? "conflicting-original-actor" : original.OriginalActorId,
                SubjectId = originalStatus == "payload" ? "conflicting-original-actor" : original.ExecutingActorId,
                OccurredUtc = original.OccurredUtc, Outcome = SecurityAuditOutcome.Succeeded,
                PolicyDecision = EMF.Security.Authorization.AuthorizationDecision.Allow,
                Facts = new Dictionary<string, string> { ["classificationId"] = original.ClassificationId.Value,
                    ["classificationRevision"] = original.ClassificationRevision.Value, ["disposition"] = "Created", ["ingestionSchemaVersion"] = "1" }
            });
        }
        await f.SqlAsync("DELETE FROM ArtifactIngestionAudit");
        var tracing = new ReceiptAuditTrace(f.Audit, originalStatus == "unavailable" ? intent.CreateEventId.Value : null);
        await f.Restart(audit: tracing).ReconcileReceiptsAsync(null, 100);
        Assert.Contains(intent.CreateEventId.Value, tracing.Queries);
        Assert.DoesNotContain(intent.CreateEventId.Value, tracing.Appends);
        Assert.Equal(review, await f.Persistence.ReadReviewAuditObligationAsync(intent.OperationId));
        Assert.Equal(1, (await f.Persistence.ReadRecoveryHealthAsync()).RequiresReviewCount);
        Assert.Equal(1, (await f.Persistence.ReadRecoveryHealthAsync()).OrphanReceiptCount);
        if (originalStatus is "conflicting" or "payload")
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = f.DatabasePath, Pooling = false }.ToString());
            await connection.OpenAsync(); using var command = connection.CreateCommand();
            command.CommandText = "SELECT Category FROM ArtifactIngestionReviews";
            Assert.Equal("ReceiptAuditConflict", await command.ExecuteScalarAsync());
        }
        if (originalStatus == "missing") Assert.Null(await f.Audit.FindVerifiedAsync(new(intent.CreateEventId.Value)));
        var result = await f.Restart().RecoverOperationAsync(intent.OperationId);
        Assert.True(result.IsAdopted); Assert.Equal(IngestionAuditDelivery.RequiresReview, result.AuditDelivery);
        Assert.Equal(ArtifactIngestionState.RequiresReview, result.State);
        Assert.NotNull(await f.Physical.ReadAsync(f.Id));
        Assert.Null(await f.Physical.GetMutationOutcomeAsync(intent.CleanupOperationId));
        Assert.NotEmpty(await f.Physical.ReadAuditObligationsAsync(null, 100));
    }
    [Fact]
    public async Task Authorized_receipt_reconciliation_with_valid_context_and_matching_acknowledgement_completes()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        await f.Service().IngestAsync(f.Draft, f.Content);
        var intent = await f.IntentAsync();
        await f.SqlAsync("DELETE FROM ArtifactIngestionAudit");
        var tracing = new ReceiptAuditTrace(f.Audit);
        await f.Restart(audit: tracing).ReconcileReceiptsAsync(null, 100);
        Assert.Contains(intent.CreateEventId.Value, tracing.Queries);
        Assert.Empty(tracing.Appends); // Matching canonical acknowledgements are recognized retries.
        Assert.Equal(ArtifactIngestionState.Completed, (await f.IntentAsync()).State);
        Assert.Equal(0, (await f.Persistence.ReadRecoveryHealthAsync()).PendingAuditCount);
        Assert.Equal(0, (await f.Persistence.ReadRecoveryHealthAsync()).RequiresReviewCount);
        Assert.NotNull(await f.Physical.ReadAsync(f.Id));
    }
    private sealed class ReceiptAuditTrace(IAcknowledgedSecurityAuditSink inner, string? unavailableEvent = null) : IAcknowledgedSecurityAuditSink
    {
        public List<string> Queries { get; } = [];
        public List<string> Appends { get; } = [];
        public Task WriteAsync(SecurityAuditRecord record, CancellationToken ct = default) => inner.WriteAsync(record, ct);
        public Task<SecurityAuditAcknowledgement> AppendAsync(SecurityAuditRecord record, CancellationToken ct = default)
        { Appends.Add(record.AuditEventId!.Value.Value); return inner.AppendAsync(record, ct); }
        public Task<VerifiedSecurityAuditEvent?> FindVerifiedAsync(EMF.Security.Auditing.Models.SecurityAuditEventId id, CancellationToken ct = default)
        {
            Queries.Add(id.Value);
            return id.Value == unavailableEvent ? throw new IOException("Synthetic original-event lookup outage") : inner.FindVerifiedAsync(id, ct);
        }
    }
    [Fact]
    public async Task Review_preserves_recovery_role_even_when_the_authenticated_principal_matches_original_actor()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.Service().IngestAsync(f.Draft, f.Content);
        f.SecurityContext.RecoveryActor = f.SecurityContext.Operation.ActorId;
        await f.SqlAsync("DELETE FROM ArtifactIngestionIntents");
        var outcome = await f.Restart().RecoverOperationAsync(f.SecurityContext.Operation.OperationId);
        Assert.True(outcome.IsAdopted); Assert.Equal(ArtifactIngestionState.RequiresReview, outcome.State);
        var review = Assert.Single((await f.Persistence.ReadAuditObligationsAsync(outcome.OperationId)).Where(x => x.Action == IngestionAuditAction.RequiresReview));
        Assert.True(review.IsRecovery);
        var canonical = (await f.Audit.FindVerifiedAsync(new(review.EventId.Value)))!.Record;
        Assert.Equal(f.SecurityContext.Operation.ActorId, canonical.OriginalActorId);
        Assert.Equal(f.SecurityContext.RecoveryActor, canonical.RecoveryActorId);
        Assert.NotNull(await f.Physical.ReadAsync(f.Id));
    }
    [Fact]
    public async Task Changed_review_condition_is_canonical_identity_conflict_and_unknown_schema_is_rejected()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        await f.Service().IngestAsync(f.Draft, f.Content);
        await f.SqlAsync("DELETE FROM ArtifactIngestionIntents");
        await f.Restart().RecoverOperationAsync(f.SecurityContext.Operation.OperationId);
        var obligation = Assert.Single((await f.Persistence.ReadAuditObligationsAsync(f.SecurityContext.Operation.OperationId))
            .Where(x => x.Action == IngestionAuditAction.RequiresReview));
        var canonical = (await f.Audit.FindVerifiedAsync(new(obligation.EventId.Value)))!.Record;
        var facts = canonical.Facts.ToDictionary(x => x.Key, x => x.Value);
        facts["recoveryCondition"] = "ContradictoryAdoption";
        var changed = CopyWithFacts(canonical, facts);
        Assert.False(SecurityAuditCanonicalEvent.Encode(canonical).SequenceEqual(SecurityAuditCanonicalEvent.Encode(changed)));
        await Assert.ThrowsAsync<SecurityAuditIdentityConflictException>(() => f.Audit.AppendAsync(changed));
        facts["ingestionSchemaVersion"] = "2";
        Assert.Throws<ArgumentException>(() => SecurityAuditCanonicalEvent.Encode(CopyWithFacts(canonical, facts)));
        await Assert.ThrowsAsync<SqliteException>(() => f.SqlAsync("UPDATE ArtifactIngestionReviews SET ReviewJson=NULL"));
        await Assert.ThrowsAsync<SqliteException>(() => f.SqlAsync("DELETE FROM ArtifactIngestionReviews"));
        Assert.NotNull(await f.Physical.ReadAsync(f.Id));
    }
    private static async Task CreateCleanupClaimAsync(ArtifactIngestionFixture f)
    {
        await f.StopAsync("ContentCreated");
        await using var session = await f.Persistence.AcquireAsync(f.SecurityContext.Operation.OperationId);
        var intent = session.Intent!;
        await session.ClaimCleanupAsync(intent, (await session.ResolveAuthorityAsync(intent.ArtifactId))!, f.SecurityContext.RecoveryActor);
        await session.CommitAsync();
    }
    private static SecurityAuditRecord CopyWithFacts(SecurityAuditRecord record, IReadOnlyDictionary<string, string> facts) => new()
    {
        AuditEventId = record.AuditEventId, OperationId = record.OperationId, ResourceId = record.ResourceId,
        Outcome = record.Outcome, OccurredUtc = record.OccurredUtc, OriginalActorId = record.OriginalActorId,
        ServiceActorId = record.ServiceActorId, RecoveryActorId = record.RecoveryActorId, Operation = record.Operation,
        ResourceType = record.ResourceType, SubjectId = record.SubjectId, PolicyDecision = record.PolicyDecision,
        Destination = record.Destination, Facts = facts
    };
    [Fact]
    public async Task Missing_classification_requires_separately_authorized_non_destructive_review()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.StopAsync("ContentCreated");
        await f.SqlAsync("DELETE FROM ArtifactMutationAuthority"); f.SecurityContext.AllowReview = false;
        await Assert.ThrowsAsync<ArtifactIngestionReviewException>(() => f.Restart().RecoverOperationAsync(f.SecurityContext.Operation.OperationId));
        Assert.Equal(ArtifactIngestionState.ContentCreated, (await f.IntentAsync()).State);
        Assert.Contains(f.SecurityContext.Reviews, x => x.Actor == f.SecurityContext.RecoveryActor
            && x.Artifact == f.Id && x.Operation == f.SecurityContext.Operation.OperationId);
        Assert.NotNull(await f.Physical.ReadAsync(f.Id));
        f.SecurityContext.AllowReview = true;
        Assert.Equal(ArtifactIngestionState.RequiresReview, (await f.Restart().RecoverOperationAsync(f.SecurityContext.Operation.OperationId)).State);
    }
    [Fact]
    public async Task Invented_delivery_row_without_immutable_event_facts_is_never_canonicalized()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.Service().IngestAsync(f.Draft, f.Content);
        var intent = await f.IntentAsync();
        var original = Assert.Single((await f.Persistence.ReadAuditObligationsAsync(intent.OperationId)).Where(x => x.Action == IngestionAuditAction.Created));
        var invented = original with { EventId = new("synthetic-invented-event"), OriginalActorId = "fabricated-actor", ExecutingActorId = "fabricated-actor" };
        await f.SqlAsync("INSERT INTO ArtifactIngestionAudit VALUES($event,$op,$json,0)", ("$event", invented.EventId.Value),
            ("$op", intent.OperationId.Value), ("$json", System.Text.Json.JsonSerializer.Serialize(invented)));
        var outcome = await f.Restart().RecoverOperationAsync(intent.OperationId);
        Assert.True(outcome.IsAdopted); Assert.Equal(ArtifactIngestionState.RequiresReview, outcome.State);
        Assert.Null(await f.Audit.FindVerifiedAsync(new(invented.EventId.Value))); Assert.NotNull(await f.Physical.ReadAsync(f.Id));
    }
    [Fact]
    public async Task No_content_decision_delivery_is_reconstructed_with_its_original_time_and_actor()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.StopAsync("Prepared");
        var recovered = await f.Restart(audit: new AuditOutage()).RecoverOperationAsync(f.SecurityContext.Operation.OperationId);
        var decision = Assert.Single(await f.Persistence.ReadAuditObligationsAsync(recovered.OperationId));
        Assert.Equal(IngestionAuditAction.NoContent, decision.Action);
        await f.SqlAsync("DELETE FROM ArtifactIngestionAudit");
        Assert.Contains(await f.Persistence.ReadRecoveryWorkAsync(0, 100), x => x.OperationId == recovered.OperationId);
        await f.Restart().RecoverOperationAsync(recovered.OperationId);
        Assert.Equal(decision, Assert.Single(await f.Persistence.ReadAuditObligationsAsync(recovered.OperationId)));
        var canonical = await f.Audit.FindVerifiedAsync(new(decision.EventId.Value)); Assert.NotNull(canonical);
        Assert.Equal(decision.OccurredUtc, canonical.Record.OccurredUtc); Assert.Equal(decision.ExecutingActorId, canonical.Record.RecoveryActorId);
        Assert.Empty(await f.Physical.ReadAuditObligationsAsync(null, 100));
        await Assert.ThrowsAsync<SqliteException>(() => f.SqlAsync("UPDATE ArtifactIngestionAuditFacts SET ObligationJson='broken'"));
        await Assert.ThrowsAsync<SqliteException>(() => f.SqlAsync("DELETE FROM ArtifactIngestionAuditFacts"));
    }
    [Fact]
    public async Task Valid_json_with_fabricated_original_actor_is_not_delivered()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        await f.Service(audit: new AuditOutage()).IngestAsync(f.Draft, f.Content);
        var intent = await f.IntentAsync();
        await f.SqlAsync("UPDATE ArtifactIngestionAudit SET ObligationJson=json_set(ObligationJson,'$.OriginalActorId','fabricated-actor') WHERE EventId=$event",
            ("$event", intent.CreateEventId.Value));
        var outcome = await f.Restart().RecoverOperationAsync(intent.OperationId);
        Assert.True(outcome.IsAdopted); Assert.Equal(ArtifactIngestionState.RequiresReview, outcome.State);
        Assert.Null(await f.Audit.FindVerifiedAsync(new(intent.CreateEventId.Value)));
        Assert.NotNull(await f.Physical.ReadAsync(f.Id));
    }
    [Fact]
    public async Task Failure_between_claim_history_and_outbox_writes_rolls_back_the_entire_claim()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.StopAsync("ContentCreated");
        await f.SqlAsync("CREATE TRIGGER SyntheticRejectClaimAudit BEFORE INSERT ON ArtifactIngestionAudit WHEN json_extract(NEW.ObligationJson,'$.Action')=3 BEGIN SELECT RAISE(ABORT,'synthetic audit write interruption'); END;");
        await using (var session = await f.Persistence.AcquireAsync(f.SecurityContext.Operation.OperationId))
        {
            var intent = session.Intent!;
            var authority = (await session.ResolveAuthorityAsync(intent.ArtifactId))!;
            await Assert.ThrowsAsync<SqliteException>(() => session.ClaimCleanupAsync(intent, authority, f.SecurityContext.RecoveryActor));
        }
        Assert.Equal(ArtifactIngestionState.ContentCreated, (await f.IntentAsync()).State);
        Assert.DoesNotContain(await f.Persistence.ReadAuditObligationsAsync(f.SecurityContext.Operation.OperationId), x => x.Action == IngestionAuditAction.CleanupClaimed);
        await f.SqlAsync("DROP TRIGGER SyntheticRejectClaimAudit");
        Assert.Equal(ArtifactIngestionState.Cleaned, (await f.Restart().RecoverOperationAsync(f.SecurityContext.Operation.OperationId)).State);
        Assert.Single((await f.Persistence.ReadAuditObligationsAsync(f.SecurityContext.Operation.OperationId)).Where(x => x.Action == IngestionAuditAction.CleanupClaimed));
    }
    [Fact]
    public async Task Lost_cleanup_claim_delivery_is_reconstructed_from_immutable_authorization_context()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await CreateCleanupClaimAsync(f);
        var intent = await f.IntentAsync();
        var claim = Assert.Single((await f.Persistence.ReadAuditObligationsAsync(intent.OperationId))
            .Where(x => x.Action == IngestionAuditAction.CleanupClaimed));
        await f.SqlAsync("DELETE FROM ArtifactIngestionAudit WHERE EventId=$event", ("$event", claim.EventId.Value));
        var outcome = await f.Restart().RecoverOperationAsync(intent.OperationId);
        Assert.Equal(ArtifactIngestionState.Cleaned, outcome.State);
        Assert.Equal(claim, Assert.Single((await f.Persistence.ReadAuditObligationsAsync(intent.OperationId))
            .Where(x => x.Action == IngestionAuditAction.CleanupClaimed)));
        var canonical = (await f.Audit.FindVerifiedAsync(new(claim.EventId.Value)))!.Record;
        Assert.Equal(claim.OccurredUtc, canonical.OccurredUtc); Assert.Equal(claim.ExecutingActorId, canonical.RecoveryActorId);
        await Assert.ThrowsAsync<SqliteException>(() => f.SqlAsync("DELETE FROM ArtifactIngestionCleanupClaims"));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_corrupt_immutable_claim_with_surviving_mutable_claim_requires_review(bool corrupt)
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await CreateCleanupClaimAsync(f);
        await f.SqlAsync(corrupt
            ? "DROP TRIGGER ArtifactIngestionCleanupClaimNoUpdate; UPDATE ArtifactIngestionCleanupClaims SET ClaimJson='broken'; CREATE TRIGGER ArtifactIngestionCleanupClaimNoUpdate BEFORE UPDATE ON ArtifactIngestionCleanupClaims BEGIN SELECT RAISE(ABORT,'Immutable ingestion cleanup claim'); END;"
            : "DROP TRIGGER ArtifactIngestionCleanupClaimNoDelete; DELETE FROM ArtifactIngestionCleanupClaims; CREATE TRIGGER ArtifactIngestionCleanupClaimNoDelete BEFORE DELETE ON ArtifactIngestionCleanupClaims BEGIN SELECT RAISE(ABORT,'Immutable ingestion cleanup claim'); END;");
        var intent = await f.IntentAsync(); Assert.Equal(ArtifactIngestionState.CleanupClaimed, intent.State);
        var outcome = await f.Restart().RecoverOperationAsync(intent.OperationId);
        Assert.Equal(ArtifactIngestionState.RequiresReview, outcome.State);
        Assert.NotNull(await f.Physical.ReadAsync(f.Id)); Assert.Null(await f.Physical.GetMutationOutcomeAsync(intent.CleanupOperationId));
    }
    [Theory]
    [InlineData("actor")]
    [InlineData("time")]
    [InlineData("condition")]
    public async Task Changed_cleanup_claim_delivery_payload_is_integrity_conflict_before_delete(string field)
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await CreateCleanupClaimAsync(f);
        var intent = await f.IntentAsync();
        var claim = Assert.Single((await f.Persistence.ReadAuditObligationsAsync(intent.OperationId)).Where(x => x.Action == IngestionAuditAction.CleanupClaimed));
        var changed = field switch
        {
            "actor" => claim with { ExecutingActorId = "fabricated-recovery" },
            "time" => claim with { OccurredUtc = claim.OccurredUtc.AddTicks(1) },
            _ => claim with { SafeFailureCategory = "LifecycleDamage" }
        };
        await f.SqlAsync("UPDATE ArtifactIngestionAudit SET ObligationJson=$json WHERE EventId=$event",
            ("$json", System.Text.Json.JsonSerializer.Serialize(changed)), ("$event", claim.EventId.Value));
        var outcome = await f.Restart().RecoverOperationAsync(intent.OperationId);
        Assert.Equal(ArtifactIngestionState.RequiresReview, outcome.State);
        Assert.NotNull(await f.Physical.ReadAsync(f.Id)); Assert.Null(await f.Physical.GetMutationOutcomeAsync(intent.CleanupOperationId));
        Assert.Null(await f.Audit.FindVerifiedAsync(new(claim.EventId.Value)));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rewinding_mutable_state_cannot_hide_committed_claim_history_or_recreate_a_missing_claim(bool removeClaim)
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.StopAsync("ContentCreated");
        var stale = await f.IntentAsync();
        await using (var session = await f.Persistence.AcquireAsync(stale.OperationId))
        { await session.ClaimCleanupAsync(stale, (await session.ResolveAuthorityAsync(f.Id))!, f.SecurityContext.RecoveryActor); await session.CommitAsync(); }
        var claim = Assert.Single((await f.Persistence.ReadAuditObligationsAsync(stale.OperationId)).Where(x => x.Action == IngestionAuditAction.CleanupClaimed));
        await f.SqlAsync("UPDATE ArtifactIngestionIntents SET State=$state,Revision=$rev,IntentJson=$json", ("$state", (int)stale.State),
            ("$rev", stale.Revision), ("$json", System.Text.Json.JsonSerializer.Serialize(stale)));
        if (removeClaim)
            await f.SqlAsync("DROP TRIGGER ArtifactIngestionCleanupClaimNoDelete; DELETE FROM ArtifactIngestionCleanupClaims; CREATE TRIGGER ArtifactIngestionCleanupClaimNoDelete BEFORE DELETE ON ArtifactIngestionCleanupClaims BEGIN SELECT RAISE(ABORT,'Immutable ingestion cleanup claim'); END;");
        var outcome = await f.Restart().RecoverOperationAsync(stale.OperationId);
        Assert.Equal(ArtifactIngestionState.RequiresReview, outcome.State);
        Assert.Equal(claim, Assert.Single((await f.Persistence.ReadAuditObligationsAsync(stale.OperationId)).Where(x => x.Action == IngestionAuditAction.CleanupClaimed)));
        Assert.NotNull(await f.Physical.ReadAsync(f.Id)); Assert.Null(await f.Physical.GetMutationOutcomeAsync(stale.CleanupOperationId));
    }
    [Fact]
    public async Task Mutable_review_reason_cannot_override_immutable_review_condition_to_enable_cleanup()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.StopAsync("ContentCreated");
        await using (var session = await f.Persistence.AcquireAsync(f.SecurityContext.Operation.OperationId))
        { await session.RecordReviewAsync("LifecycleDamage", f.SecurityContext.RecoveryActor); await session.CommitAsync(); }
        var review = await f.Persistence.ReadReviewAuditObligationAsync(f.SecurityContext.Operation.OperationId);
        var intent = await f.IntentAsync();
        var damaged = intent with { State = ArtifactIngestionState.ContentCreated, Disposition = ArtifactIngestionDisposition.Conflict,
            SafeFailureCategory = "CanonicalReconciliationFailure" };
        await f.SqlAsync("UPDATE ArtifactIngestionReviews SET Category='CanonicalReconciliationFailure'; UPDATE ArtifactIngestionIntents SET State=$state,IntentJson=$json",
            ("$state", (int)damaged.State), ("$json", System.Text.Json.JsonSerializer.Serialize(damaged)));
        await using (var session = await f.Persistence.AcquireAsync(intent.OperationId))
        {
            var authority = (await session.ResolveAuthorityAsync(f.Id))!;
            await Assert.ThrowsAsync<InvalidDataException>(() => session.ClaimCleanupAsync(session.Intent!, authority, f.SecurityContext.RecoveryActor));
        }
        Assert.Equal(review, await f.Persistence.ReadReviewAuditObligationAsync(intent.OperationId));
        Assert.NotNull(await f.Physical.ReadAsync(f.Id)); Assert.Null(await f.Physical.GetMutationOutcomeAsync(intent.CleanupOperationId));
    }
    [Theory]
    [InlineData("event")]
    [InlineData("time")]
    public async Task Contradictory_immutable_claim_payload_requires_review_without_delete(string field)
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await CreateCleanupClaimAsync(f);
        var intent = await f.IntentAsync();
        var update = field == "event" ? "UPDATE ArtifactIngestionCleanupClaims SET ClaimJson=json_set(ClaimJson,'$.EventId.Value','substituted-claim-event');"
            : "UPDATE ArtifactIngestionCleanupClaims SET ClaimJson=json_set(ClaimJson,'$.OccurredUtc','2000-01-01T00:00:00+00:00');";
        await f.SqlAsync("DROP TRIGGER ArtifactIngestionCleanupClaimNoUpdate; " + update +
            " CREATE TRIGGER ArtifactIngestionCleanupClaimNoUpdate BEFORE UPDATE ON ArtifactIngestionCleanupClaims BEGIN SELECT RAISE(ABORT,'Immutable ingestion cleanup claim'); END;");
        var outcome = await f.Restart().RecoverOperationAsync(intent.OperationId);
        Assert.Equal(ArtifactIngestionState.RequiresReview, outcome.State);
        Assert.NotNull(await f.Physical.ReadAsync(f.Id)); Assert.Null(await f.Physical.GetMutationOutcomeAsync(intent.CleanupOperationId));
    }
    [Fact]
    public async Task Corrupt_cleanup_actor_requires_review_without_delete()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await CreateCleanupClaimAsync(f);
        await f.SqlAsync("UPDATE ArtifactIngestionIntents SET IntentJson=json_set(IntentJson,'$.CleanupActorId','fabricated-recovery')");
        var outcome = await f.Restart().RecoverOperationAsync(f.SecurityContext.Operation.OperationId);
        Assert.Equal(ArtifactIngestionState.RequiresReview, outcome.State);
        Assert.NotNull(await f.Physical.ReadAsync(f.Id));
        Assert.Null(await f.Physical.GetMutationOutcomeAsync((await f.IntentAsync()).CleanupOperationId));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Independent_receipt_scan_survives_missing_or_corrupt_intent_and_deleted_outbox(bool corrupt)
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        var result = await f.Service(audit: new AuditOutage()).IngestAsync(f.Draft, f.Content);
        var intent = await f.IntentAsync();
        await f.SqlAsync(corrupt ? "UPDATE ArtifactIngestionIntents SET IntentJson='broken'" : "DELETE FROM ArtifactIngestionIntents");
        await f.SqlAsync("DELETE FROM ArtifactIngestionAudit");
        var cursor = await f.Restart(audit: new AuditOutage()).ReconcileReceiptsAsync(null, 100);
        Assert.NotNull(cursor);
        var health = await f.Persistence.ReadRecoveryHealthAsync(); Assert.Equal(1, health.OrphanReceiptCount); Assert.Equal(1, health.RequiresReviewCount);
        Assert.Null(await f.Audit.FindVerifiedAsync(new(intent.CreateEventId.Value))); // no fabricated original outcome
        var recovered = await f.Restart().RecoverOperationAsync(result.OperationId);
        Assert.True(recovered.IsAdopted); Assert.Equal(ArtifactIngestionState.RequiresReview, recovered.State); Assert.NotNull(await f.Physical.ReadAsync(f.Id));
    }
    [Fact]
    public async Task Physical_receipt_prefix_detects_obligation_even_if_all_mutable_and_original_context_rows_are_lost()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        var result = await f.Service(audit: new AuditOutage()).IngestAsync(f.Draft, f.Content);
        await f.SqlAsync("DROP TRIGGER ArtifactIngestionOperationNoDelete; DELETE FROM ArtifactIngestionOperations; DELETE FROM ArtifactIngestionIntents; DELETE FROM ArtifactIngestionAudit; CREATE TRIGGER ArtifactIngestionOperationNoDelete BEFORE DELETE ON ArtifactIngestionOperations BEGIN SELECT RAISE(ABORT,'Immutable ingestion operation binding'); END;");
        var cursor = await f.Restart().ReconcileReceiptsAsync(null, 100);
        Assert.NotNull(cursor); Assert.Equal(1, (await f.Persistence.ReadRecoveryHealthAsync()).OrphanReceiptCount);
        var recovered = await f.Restart().RecoverOperationAsync(result.OperationId);
        Assert.True(recovered.IsAdopted); Assert.Equal(ArtifactIngestionState.RequiresReview, recovered.State);
        Assert.NotNull(await f.Physical.ReadAsync(f.Id));
    }
    [Fact]
    public async Task Corrupt_pending_audit_obligation_requires_review_and_preserves_adoption()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); var result = await f.Service(audit: new AuditOutage()).IngestAsync(f.Draft, f.Content);
        var intent = await f.IntentAsync();
        await f.SqlAsync("UPDATE ArtifactIngestionAudit SET ObligationJson='broken' WHERE EventId=$event", ("$event", intent.CreateEventId.Value));
        await f.Restart().ReconcileReceiptsAsync(null, 100);
        var recovered = await f.Restart().RecoverOperationAsync(result.OperationId);
        Assert.True(recovered.IsAdopted); Assert.Equal(ArtifactIngestionState.RequiresReview, recovered.State);
        Assert.NotNull(await f.Physical.ReadAsync(f.Id)); Assert.Null(await f.Audit.FindVerifiedAsync(new(intent.CreateEventId.Value)));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_required_creation_or_adoption_audit_work_is_reconstructed_from_valid_durable_context(bool adoption)
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); var result = await f.Service(audit: new AuditOutage()).IngestAsync(f.Draft, f.Content);
        var intent = await f.IntentAsync(); var eventId = adoption ? intent.AdoptionEventId : intent.CreateEventId;
        await f.SqlAsync("DELETE FROM ArtifactIngestionAudit WHERE EventId=$event", ("$event", eventId.Value));
        await f.Restart().ReconcileReceiptsAsync(null, 100);
        var recovered = await f.Restart().RecoverOperationAsync(result.OperationId);
        Assert.True(recovered.IsAdopted); Assert.Equal(ArtifactIngestionState.Completed, recovered.State);
        var verified = await f.Audit.FindVerifiedAsync(new(eventId.Value)); Assert.NotNull(verified);
        Assert.Equal(f.SecurityContext.Operation.ActorId, verified.Record.OriginalActorId);
        Assert.Equal(adoption ? intent.AdoptionUtc : intent.CreateReceipt!.OccurredUtc, verified.Record.OccurredUtc);
        Assert.Equal(0, (await f.Persistence.ReadRecoveryHealthAsync()).OrphanReceiptCount);
    }
    [Fact]
    public async Task Lost_delete_journal_audit_row_is_found_through_cleanup_receipt_identity()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.StopAsync("ContentCreated");
        await f.Restart(audit: new AuditOutage()).RecoverOperationAsync(f.SecurityContext.Operation.OperationId);
        var intent = await f.IntentAsync();
        await f.SqlAsync("DELETE FROM ArtifactIngestionAudit WHERE EventId=$event", ("$event", intent.CleanupEventId.Value));
        await f.Restart().ReconcileReceiptsAsync(null, 100);
        var verified = await f.Audit.FindVerifiedAsync(new(intent.CleanupEventId.Value)); Assert.NotNull(verified);
        Assert.Equal(intent.CleanupOperationId.Value, verified.Record.OperationId!.Value.Value);
        Assert.Equal(intent.CleanupReceipt!.OccurredUtc, verified.Record.OccurredUtc);
        Assert.Equal(ArtifactIngestionState.Cleaned, (await f.Restart().RecoverOperationAsync(intent.OperationId)).State);
    }
    [Fact]
    public async Task Lost_audit_append_response_recognizes_identical_event_without_duplicate_append()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        var result = await f.Service(audit: new LostAuditResponse(f.Audit)).IngestAsync(f.Draft, f.Content);
        Assert.True(result.IsAdopted); Assert.Equal(IngestionAuditDelivery.Pending, result.AuditDelivery);
        var intent = await f.IntentAsync(); Assert.NotNull(await f.Audit.FindVerifiedAsync(new(intent.CreateEventId.Value)));
        await f.Restart().RecoverOperationAsync(result.OperationId);
        Assert.Equal(ArtifactIngestionState.Completed, (await f.IntentAsync()).State);
        var all = await f.Persistence.ReadAuditObligationsAsync(result.OperationId);
        Assert.Equal(3, all.Count); Assert.Equal(3, (await new EMF.Security.Persistence.Sqlite.Auditing.SqliteSecurityAuditIntegrityVerifier(f.AuditPath).VerifyAsync()).ProtectedRecordCount);
    }
    [Fact]
    public async Task Mismatching_canonical_acknowledgement_requires_review_without_clearing_adoption()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        var result = await f.Service(audit: new WrongAuditAcknowledgement()).IngestAsync(f.Draft, f.Content);
        Assert.True(result.IsAdopted); Assert.Equal(IngestionAuditDelivery.RequiresReview, result.AuditDelivery);
        Assert.Equal(ArtifactIngestionState.RequiresReview, result.State); Assert.NotNull(await f.Physical.ReadAsync(f.Id));
    }
    [Fact]
    public async Task Journal_only_delivered_flags_do_not_substitute_for_verified_canonical_acknowledgement()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); var result = await f.Service(audit: new AuditOutage()).IngestAsync(f.Draft, f.Content);
        await f.SqlAsync("UPDATE ArtifactIngestionAudit SET Delivered=1");
        var recovered = await f.Restart(audit: new AuditOutage()).RecoverOperationAsync(result.OperationId);
        Assert.True(recovered.IsAdopted); Assert.Equal(ArtifactIngestionState.MetadataCommitted, recovered.State);
        Assert.Equal(IngestionAuditDelivery.Pending, recovered.AuditDelivery);
    }
    [Fact]
    public async Task Direct_replace_cannot_bypass_immutable_adoption_triggers()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.Service().IngestAsync(f.Draft, f.Content);
        await Assert.ThrowsAsync<SqliteException>(() => f.SqlAsync("INSERT OR REPLACE INTO ArtifactIngestionAdoptions SELECT * FROM ArtifactIngestionAdoptions"));
    }
    [Fact]
    public async Task Uncoordinated_metadata_writer_cannot_adopt_provisional_content()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.StopAsync("ContentCreated");
        await Assert.ThrowsAsync<SqliteException>(() => f.Repository.AddArtifactWithProvenanceAsync(f.Draft.Artifact, f.Draft.Provenance));
        Assert.Null(await f.Repository.GetArtifactAsync(f.Id));
        Assert.Equal(ArtifactIngestionState.Cleaned, (await f.Restart().RecoverOperationAsync(f.SecurityContext.Operation.OperationId)).State);
    }
    [Fact]
    public async Task Concurrent_retries_across_independent_store_instances_issue_one_candidate_and_receipt()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        var results = await Task.WhenAll(Task.Run(() => f.Service().IngestAsync(f.Draft, f.Content)), Task.Run(() => f.Restart().IngestAsync(f.Draft, f.Content)));
        Assert.All(results, r => Assert.True(r.IsAdopted)); Assert.Equal(1, f.Encryption.Encryptions);
        Assert.Single(await f.Physical.ReadAuditObligationsAsync(null, 100));
    }
    [Fact]
    public async Task Reclassification_writer_waits_for_authorization_and_physical_cleanup_fence()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.StopAsync("ContentCreated");
        Task? reclassification = null; var calls = 0;
        f.Authorization.Hook = async request =>
        {
            if (request.PermissionId == EMF.Security.Models.SecurityPermissions.ArtifactIngestionRecover && ++calls == 2)
            {
                reclassification = Task.Run(() => f.SqlAsync("UPDATE ArtifactMutationAuthority SET ClassificationRevision='raced',ClassificationId='Restricted'"));
                await Task.Delay(100); Assert.False(reclassification.IsCompleted);
            }
        };
        var result = await f.Restart().RecoverOperationAsync(f.SecurityContext.Operation.OperationId);
        Assert.Equal(ArtifactIngestionState.Cleaned, result.State); Assert.NotNull(reclassification); await reclassification;
        Assert.Null(await f.Physical.ReadAsync(f.Id));
    }
    [Fact]
    public async Task Service_preserves_caller_cancellation_when_bounded_cleanup_fails()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); using var cancelled = new CancellationTokenSource();
        var store = new FaultStore(f.Physical) { AfterCreate = () => { cancelled.Cancel(); return Task.CompletedTask; }, FailDelete = true };
        var service = new EvidenceFileIngestionService(f.Service(physical: store), f.Fingerprints, new FixedId(f.Id), new ArtifactFactory());
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.IngestAsync(f.SourcePath, cancelled.Token));
        Assert.DoesNotContain("synthetic cleanup failure", error.Message);
        Assert.Equal(ArtifactIngestionState.RequiresReview, (await f.IntentAsync()).State); Assert.NotNull(await f.Physical.ReadAsync(f.Id));
    }
    [Fact]
    public async Task Service_reports_adoption_when_cancellation_arrives_after_commit()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        var fault = new FaultPersistence(f.Persistence, "MetadataCommitted");
        var service = new EvidenceFileIngestionService(f.Service(persistence: fault), f.Fingerprints, new FixedId(f.Id), new ArtifactFactory());
        var result = await service.IngestAsync(f.SourcePath);
        Assert.True(result.IsAdopted); Assert.Equal(ArtifactIngestionState.Completed, result.LifecycleState); Assert.NotNull(await f.Physical.ReadAsync(f.Id));
    }
    [Fact]
    public async Task Metadata_exception_uses_independent_cleanup_budget_and_exposes_no_provider_details()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        var fault = new FaultPersistence(f.Persistence, "MetadataCommitted", true) { OnFault = () => throw new IOException("synthetic-sensitive-provider-detail") };
        var service = new EvidenceFileIngestionService(f.Service(persistence: fault), f.Fingerprints, new FixedId(f.Id), new ArtifactFactory());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.IngestAsync(f.SourcePath));
        Assert.DoesNotContain("synthetic-sensitive", error.Message); Assert.Null(await f.Physical.ReadAsync(f.Id));
        Assert.Equal(ArtifactIngestionState.Cleaned, (await f.IntentAsync()).State);
    }
    [Fact]
    public async Task Corrupt_original_operation_binding_requires_review_without_deletion_or_fabricated_actor()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.StopAsync("ContentCreated");
        await f.SqlAsync("DROP TRIGGER ArtifactIngestionOperationNoUpdate; UPDATE ArtifactIngestionOperations SET BindingJson='broken'; CREATE TRIGGER ArtifactIngestionOperationNoUpdate BEFORE UPDATE ON ArtifactIngestionOperations BEGIN SELECT RAISE(ABORT,'Immutable ingestion operation binding'); END;");
        var result = await f.Restart().RecoverOperationAsync(f.SecurityContext.Operation.OperationId);
        Assert.Equal(ArtifactIngestionState.RequiresReview, result.State); Assert.NotNull(await f.Physical.ReadAsync(f.Id));
    }
    [Fact]
    public async Task Missing_admitted_candidate_requires_review_without_second_randomized_encryption()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.StopAsync("Candidate");
        foreach (var candidate in Directory.GetFiles(Path.Combine(f.Root, "staging"), "*.candidate")) File.Delete(candidate);
        var result = await f.Restart().RecoverOperationAsync(f.SecurityContext.Operation.OperationId);
        Assert.Equal(ArtifactIngestionState.RequiresReview, result.State); Assert.Equal(1, f.Encryption.Encryptions); Assert.Null(await f.Physical.ReadAsync(f.Id));
    }
    [Fact]
    public async Task Unknown_future_ingestion_schema_is_rejected()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.SqlAsync("INSERT INTO ArtifactIngestionSchema VALUES(2)");
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Persistence.InitializeAsync());
    }
    [Fact]
    public async Task Unsupported_metadata_journal_mode_fails_admission()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.SqlAsync("PRAGMA journal_mode=WAL");
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Persistence.AcquireAsync(f.SecurityContext.Operation.OperationId));
    }
    [Theory]
    [InlineData("operation")]
    [InlineData("resource")]
    [InlineData("outcome")]
    [InlineData("time")]
    public async Task Same_event_identity_with_conflicting_canonical_tuple_is_integrity_conflict(string field)
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        var committed = await f.Service().IngestAsync(f.Draft, f.Content);
        var result = await f.Restart(audit: new ConflictingCanonicalEvent(f.Audit, field)).RecoverOperationAsync(committed.OperationId);
        Assert.True(result.IsAdopted); Assert.Equal(IngestionAuditDelivery.RequiresReview, result.AuditDelivery);
        Assert.Equal(ArtifactIngestionState.RequiresReview, result.State); Assert.NotNull(await f.Physical.ReadAsync(f.Id));
        Assert.NotEmpty(await f.Physical.ReadAuditObligationsAsync(null, 100));
    }
    [Theory]
    [InlineData("pending")]
    [InlineData("review")]
    [InlineData("unknown")]
    public async Task Generation_reclamation_never_prunes_unacknowledged_or_review_receipts(string status)
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.StopAsync("ContentCreated");
        await f.Restart(audit: new AuditOutage()).RecoverOperationAsync(f.SecurityContext.Operation.OperationId);
        var intent = await f.IntentAsync();
        if (status == "review")
        {
            await using var session = await f.Persistence.AcquireAsync(intent.OperationId);
            await session.RecordReviewAsync("CleanupOutcomeUnknown", f.SecurityContext.RecoveryActor); await session.CommitAsync();
        }
        if (status == "unknown")
        {
            await f.SqlAsync("UPDATE ArtifactIngestionAudit SET Delivered=1");
            await f.Restart(audit: new AuditOutage()).RecoverOperationAsync(intent.OperationId);
        }
        var before = await f.Physical.ReadAuditObligationsAsync(null, 100);
        var gc = new EMF.Persistence.Storage.FileSystemArtifactContentGarbageCollector(Path.Combine(f.Root, "content"));
        await gc.CollectAsync();
        Assert.Equal(before, await f.Physical.ReadAuditObligationsAsync(null, 100));
        Assert.Equal(intent.CreateReceipt, await f.Physical.GetMutationOutcomeAsync(intent.OperationId));
        Assert.Equal(intent.CleanupReceipt, await f.Physical.GetMutationOutcomeAsync(intent.CleanupOperationId));
        Assert.Null(await f.Audit.FindVerifiedAsync(new(intent.CreateEventId.Value)));
        Assert.True((await f.Persistence.ReadRecoveryHealthAsync()).PendingAuditCount > 0);
    }
    [Theory]
    [InlineData("owner")]
    [InlineData("revision")]
    [InlineData("event")]
    [InlineData("outcome")]
    public async Task Contradictory_physical_receipt_requires_review_without_delete(string field)
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.StopAsync("ContentCreated");
        var catalog = Path.Combine(f.Root, "content", ".content-catalog.sqlite");
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = catalog, Pooling = false }.ToString()))
        {
            await connection.OpenAsync(); using var command = connection.CreateCommand();
            command.CommandText = field switch
            {
                "owner" => "UPDATE ContentReceipts SET Receipt=json_set(Receipt,'$.OwnershipToken.Value','foreign-owner')",
                "revision" => "UPDATE ContentReceipts SET Receipt=json_set(Receipt,'$.CurrentRevision.Value','foreign-revision')",
                "event" => "UPDATE ContentReceipts SET Receipt=json_set(Receipt,'$.AuditEventId.Value','foreign-event')",
                _ => "UPDATE ContentReceipts SET Receipt=json_set(Receipt,'$.Outcome',3)"
            };
            await command.ExecuteNonQueryAsync();
        }
        var result = await f.Restart().RecoverOperationAsync(f.SecurityContext.Operation.OperationId);
        Assert.Equal(ArtifactIngestionState.RequiresReview, result.State);
        // The existing provider may reject damaged lineage even for a read. Surviving
        // payload files must nevertheless remain present; recovery cannot delete them.
        Assert.Single(Directory.GetFiles(Path.Combine(f.Root, "content", ".content-generations")));
    }
    private sealed class ConflictingCanonicalEvent(IAcknowledgedSecurityAuditSink inner, string field) : IAcknowledgedSecurityAuditSink
    {
        public Task WriteAsync(SecurityAuditRecord record, CancellationToken ct = default) => inner.WriteAsync(record, ct);
        public Task<SecurityAuditAcknowledgement> AppendAsync(SecurityAuditRecord record, CancellationToken ct = default) => inner.AppendAsync(record, ct);
        public async Task<VerifiedSecurityAuditEvent?> FindVerifiedAsync(SecurityAuditEventId id, CancellationToken ct = default)
        {
            var verified = await inner.FindVerifiedAsync(id, ct); if (verified is null) return null;
            var record = verified.Record;
            return new(new SecurityAuditRecord
            {
                AuditEventId = record.AuditEventId, OperationId = field == "operation" ? new("synthetic-conflicting-operation") : record.OperationId,
                ResourceId = field == "resource" ? "synthetic-conflicting-resource" : record.ResourceId,
                Outcome = field == "outcome" ? SecurityAuditOutcome.Failed : record.Outcome,
                OccurredUtc = field == "time" ? record.OccurredUtc.AddTicks(1) : record.OccurredUtc,
                OriginalActorId = record.OriginalActorId, ServiceActorId = record.ServiceActorId, RecoveryActorId = record.RecoveryActorId,
                Operation = record.Operation, ResourceType = record.ResourceType, SubjectId = record.SubjectId, PolicyDecision = record.PolicyDecision,
                Destination = record.Destination, Facts = record.Facts
            }, verified.Acknowledgement);
        }
    }
    [Fact]
    public async Task Committed_receipt_delivered_and_completed_flags_without_chain_event_reopen_delivery()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        var committed = await f.Service(audit: new AuditOutage()).IngestAsync(f.Draft, f.Content);
        var intent = await f.IntentAsync();
        var completed = intent with { State = ArtifactIngestionState.Completed };
        await f.SqlAsync("UPDATE ArtifactIngestionAudit SET Delivered=1; UPDATE ArtifactIngestionIntents SET State=$state,IntentJson=$json",
            ("$state", (int)ArtifactIngestionState.Completed), ("$json", System.Text.Json.JsonSerializer.Serialize(completed)));
        Assert.Null(await f.Audit.FindVerifiedAsync(new(intent.CreateEventId.Value)));
        var recovered = await f.Restart(audit: new AuditOutage()).RecoverOperationAsync(committed.OperationId);
        Assert.True(recovered.IsAdopted); Assert.Equal(ArtifactIngestionState.MetadataCommitted, recovered.State);
        Assert.Equal(IngestionAuditDelivery.Pending, recovered.AuditDelivery);
        Assert.True((await f.Persistence.ReadRecoveryHealthAsync()).PendingAuditCount > 0);
        var repaired = await f.Restart().RecoverOperationAsync(committed.OperationId);
        Assert.Equal(ArtifactIngestionState.Completed, repaired.State); Assert.NotNull(await f.Audit.FindVerifiedAsync(new(intent.CreateEventId.Value)));
        Assert.NotNull(await f.Physical.GetMutationOutcomeAsync(committed.OperationId));
    }
    [Fact]
    public async Task Missing_invariant_trigger_rejects_persistence_admission_and_physical_cleanup()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.StopAsync("ContentCreated");
        await f.SqlAsync("DROP TRIGGER ArtifactIngestionAdoptionNoUpdate");
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Persistence.AcquireAsync(f.SecurityContext.Operation.OperationId));
        await Assert.ThrowsAsync<ArtifactIngestionReviewException>(() => f.Restart().RecoverOperationAsync(f.SecurityContext.Operation.OperationId));
        Assert.NotNull(await f.Physical.ReadAsync(f.Id));
    }
    private sealed class FixedId(ArtifactId id) : IArtifactIdGenerator { public ArtifactId Generate() => id; }
    private sealed class LostAuditResponse(IAcknowledgedSecurityAuditSink inner) : IAcknowledgedSecurityAuditSink
    {
        public Task WriteAsync(SecurityAuditRecord record, CancellationToken ct = default) => inner.WriteAsync(record, ct);
        public Task<VerifiedSecurityAuditEvent?> FindVerifiedAsync(SecurityAuditEventId id, CancellationToken ct = default) => inner.FindVerifiedAsync(id, ct);
        public async Task<SecurityAuditAcknowledgement> AppendAsync(SecurityAuditRecord record, CancellationToken ct = default)
        { await inner.AppendAsync(record, ct); throw new IOException("synthetic lost audit acknowledgement"); }
    }
    private sealed class WrongAuditAcknowledgement : IAcknowledgedSecurityAuditSink
    {
        public Task WriteAsync(SecurityAuditRecord record, CancellationToken ct = default) => Task.CompletedTask;
        public Task<SecurityAuditAcknowledgement> AppendAsync(SecurityAuditRecord record, CancellationToken ct = default)
            => Task.FromResult(new SecurityAuditAcknowledgement(new("synthetic-wrong-event"), 1, "synthetic-wrong-hash"));
        public Task<VerifiedSecurityAuditEvent?> FindVerifiedAsync(SecurityAuditEventId id, CancellationToken ct = default) => Task.FromResult<VerifiedSecurityAuditEvent?>(null);
    }
}
