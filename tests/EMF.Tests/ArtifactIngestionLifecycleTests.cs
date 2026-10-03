using System.Text.Json;
using EMF.Core.Contracts.Ingestion;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Security.Models;
using EMF.Security.Persistence.Sqlite.Auditing;
using EMF.Tests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using static EMF.Tests.TestInfrastructure.ArtifactIngestionFixture;

namespace EMF.Tests;

public sealed class ArtifactIngestionLifecycleTests
{
    [Fact]
    public async Task Prepared_authority_precedes_creation_and_adoption_is_atomic()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        await f.StopAsync("Prepared");
        var prepared = await f.IntentAsync();
        Assert.Null(await f.Physical.ReadAsync(f.Id)); Assert.Null(await f.Repository.GetArtifactAsync(f.Id));
        await using (var session = await f.Persistence.AcquireAsync(prepared.OperationId))
        {
            var authority = await session.ResolveAuthorityAsync(f.Id);
            Assert.NotNull(authority); Assert.False(authority.IsAdopted);
            Assert.Equal(prepared.OwnershipToken, authority.OwnershipToken);
            Assert.Equal(prepared.ClassificationRevision, authority.Revision);
        }
        var result = await f.Restart().IngestAsync(f.Draft, f.Content);
        Assert.True(result.IsAdopted); Assert.Equal(ArtifactIngestionState.Completed, result.State);
        await using var adopted = await f.Persistence.AcquireAsync(prepared.OperationId);
        var adoptedAuthority = await adopted.ResolveAuthorityAsync(f.Id);
        Assert.True(adoptedAuthority!.IsAdopted); Assert.NotEqual(prepared.ClassificationRevision, adoptedAuthority.Revision);
        Assert.True(await adopted.HasAdoptionEvidenceAsync(f.Id));
        Assert.NotNull(await f.Repository.GetArtifactAsync(f.Id));
    }
    [Theory]
    [InlineData("Prepared", false)]
    [InlineData("Candidate", false)]
    [InlineData("ContentCreated", false)]
    [InlineData("MetadataCommitted", false)]
    [InlineData("Completed", false)]
    [InlineData("MetadataCommitted", true)]
    public async Task Crash_restart_at_each_ingestion_transition_is_idempotent(string checkpoint, bool beforeCommit)
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        await f.StopAsync(checkpoint, beforeCommit);
        var result = await f.Restart().RecoverOperationAsync(f.SecurityContext.Operation.OperationId);
        var repeated = await f.Restart().RecoverOperationAsync(f.SecurityContext.Operation.OperationId);
        if (!beforeCommit && checkpoint is "MetadataCommitted" or "Completed")
        {
            Assert.True(result.IsAdopted); Assert.True(repeated.IsAdopted);
            Assert.NotNull(await f.Physical.ReadAsync(f.Id)); Assert.Equal(ArtifactIngestionState.Completed, repeated.State);
        }
        else
        {
            Assert.False(result.IsAdopted); Assert.Equal(ArtifactIngestionState.Cleaned, repeated.State);
            Assert.Null(await f.Physical.ReadAsync(f.Id)); Assert.Null(await f.Repository.GetArtifactAsync(f.Id));
        }
        Assert.Equal(result.State, repeated.State);
    }
    [Theory]
    [InlineData("CleanupClaimed")]
    [InlineData("Cleaned")]
    public async Task Crash_restart_at_cleanup_transitions_reuses_the_delete_operation(string checkpoint)
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.StopAsync("ContentCreated");
        var fault = new FaultPersistence(f.Persistence, checkpoint);
        await Assert.ThrowsAsync<SimulatedCrashException>(() => f.Service(persistence: fault).RecoverOperationAsync(f.SecurityContext.Operation.OperationId));
        var result = await f.Restart().RecoverOperationAsync(f.SecurityContext.Operation.OperationId);
        var repeated = await f.Restart().RecoverOperationAsync(f.SecurityContext.Operation.OperationId);
        Assert.Equal(ArtifactIngestionState.Cleaned, result.State); Assert.Equal(result.State, repeated.State);
        Assert.Null(await f.Physical.ReadAsync(f.Id));
        Assert.Equal(2, (await f.Physical.ReadAuditObligationsAsync(null, 100)).Count);
    }
    [Fact]
    public async Task Lost_create_response_and_replay_preserve_one_mutation_and_one_randomized_candidate()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); var store = new FaultStore(f.Physical) { LoseCreateResponse = true };
        var first = await f.Service(physical: store).IngestAsync(f.Draft, f.Content);
        var snapshot = await f.Physical.ReadVersionedAsync(f.Id);
        var replay = await f.Restart(physical: store).IngestAsync(f.Draft, f.Content);
        Assert.True(first.IsAdopted); Assert.True(replay.IsAdopted); Assert.Equal(1, store.Creates); Assert.Equal(1, f.Encryption.Encryptions);
        Assert.Equal(snapshot!.Revision, (await f.Physical.ReadVersionedAsync(f.Id))!.Revision);
        Assert.Equal(1, (await f.Physical.ReadAuditObligationsAsync(null, 100)).Count);
        Assert.True((await new SqliteSecurityAuditIntegrityVerifier(f.AuditPath).VerifyAsync()).IsValid);
    }
    [Fact]
    public async Task Restart_after_candidate_commit_does_not_encrypt_again()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.StopAsync("Candidate");
        var staged = await f.Staging.ReadAsync(f.SecurityContext.Operation.OperationId);
        Assert.NotNull(staged); Assert.Null(await f.Physical.GetMutationOutcomeAsync(f.SecurityContext.Operation.OperationId));
        var result = await f.Restart().IngestAsync(f.Draft, f.Content);
        Assert.True(result.IsAdopted); Assert.Equal(1, f.Encryption.Encryptions); Assert.Equal(staged, await f.Physical.ReadAsync(f.Id));
    }
    [Fact]
    public async Task Process_interruption_after_creation_before_journal_update_reconciles_receipt()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); var store = new FaultStore(f.Physical) { CancelAfterCreate = true };
        await Assert.ThrowsAsync<SimulatedCrashException>(() => f.Service(physical: store).IngestAsync(f.Draft, f.Content));
        Assert.Equal(ArtifactIngestionState.Prepared, (await f.IntentAsync()).State);
        var result = await f.Restart().RecoverOperationAsync(f.SecurityContext.Operation.OperationId);
        Assert.Equal(ArtifactIngestionState.Cleaned, result.State); Assert.Equal(1, store.Creates); Assert.Equal(1, f.Encryption.Encryptions);
        Assert.Null(await f.Physical.ReadAsync(f.Id));
    }
    [Fact]
    public async Task Lost_delete_response_reconciles_and_repeated_recovery_never_deletes_twice()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.StopAsync("ContentCreated");
        var store = new FaultStore(f.Physical) { LoseDeleteResponse = true };
        var first = await f.Restart(physical: store).RecoverOperationAsync(f.SecurityContext.Operation.OperationId);
        var repeated = await f.Restart(physical: store).RecoverOperationAsync(f.SecurityContext.Operation.OperationId);
        Assert.Equal(ArtifactIngestionState.Cleaned, first.State); Assert.Equal(first.State, repeated.State); Assert.Equal(1, store.Deletes);
    }
    [Fact]
    public async Task Audit_outage_after_adoption_returns_committed_and_leaves_durable_pending_delivery()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        var first = await f.Service(audit: new AuditOutage()).IngestAsync(f.Draft, f.Content);
        Assert.True(first.IsAdopted); Assert.Equal(ArtifactIngestionState.MetadataCommitted, first.State); Assert.Equal(IngestionAuditDelivery.Pending, first.AuditDelivery);
        Assert.NotEmpty(await f.Persistence.ReadAuditObligationsAsync(first.OperationId));
        var snapshot = await f.Physical.ReadVersionedAsync(f.Id);
        var recovered = await f.Restart().RecoverOperationAsync(first.OperationId);
        Assert.True(recovered.IsAdopted); Assert.Equal(ArtifactIngestionState.Completed, recovered.State);
        Assert.Equal(snapshot!.Revision, (await f.Physical.ReadVersionedAsync(f.Id))!.Revision);
    }
    [Fact]
    public async Task Stale_cleanup_claim_after_adoption_is_rejected_at_persistence_boundary()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.StopAsync("ContentCreated");
        ArtifactIngestionIntent stale; IngestionClassificationAuthority authority;
        await using (var session = await f.Persistence.AcquireAsync(f.SecurityContext.Operation.OperationId))
        { stale = session.Intent!; authority = (await session.ResolveAuthorityAsync(f.Id))!; }
        await f.Restart().IngestAsync(f.Draft, f.Content);
        await using var cleanup = await f.Persistence.AcquireAsync(stale.OperationId);
        await Assert.ThrowsAsync<InvalidDataException>(() => cleanup.ClaimCleanupAsync(stale, authority, f.SecurityContext.RecoveryActor));
        Assert.NotNull(await f.Physical.ReadAsync(f.Id));
    }
    [Fact]
    public async Task Creation_receipt_changed_after_claim_is_revalidated_before_any_delete()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.StopAsync("ContentCreated");
        var store = new FaultStore(f.Physical);
        var persistence = new FaultPersistence(f.Persistence, "CleanupClaimed")
        {
            ContinueAfterCheckpoint = true,
            OnFault = () => f.SqlAsync("UPDATE ArtifactIngestionIntents SET IntentJson=json_set(IntentJson,'$.CreateReceipt.CurrentRevision.Value','substituted-revision')")
        };
        var outcome = await f.Service(persistence: persistence, physical: store).RecoverOperationAsync(f.SecurityContext.Operation.OperationId);
        Assert.Equal(ArtifactIngestionState.RequiresReview, outcome.State); Assert.Equal(0, store.Deletes);
        Assert.NotNull(await f.Physical.ReadAsync(f.Id));
    }
    [Fact]
    public async Task Paused_worker_with_valid_committed_claim_defers_to_later_immutable_adoption_evidence()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.StopAsync("ContentCreated");
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new FaultStore(f.Physical);
        var persistence = new FaultPersistence(f.Persistence, "CleanupClaimed")
        {
            ContinueAfterCheckpoint = true,
            OnFault = async () => { paused.SetResult(); await resume.Task; }
        };
        var worker = Task.Run(() => f.Service(persistence: persistence, physical: store).RecoverOperationAsync(f.SecurityContext.Operation.OperationId));
        try
        {
            await paused.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var intent = await f.IntentAsync();
            var claim = Assert.Single((await f.Persistence.ReadAuditObligationsAsync(intent.OperationId)).Where(x => x.Action == IngestionAuditAction.CleanupClaimed));
            // Participating adopters are excluded after a claim. Inject a committed
            // inconsistent adoption transaction to model a bypassing/stale writer defect.
            var adoptionTime = DateTimeOffset.UtcNow;
            var adoptedIntent = intent with { State = ArtifactIngestionState.MetadataCommitted,
                Disposition = ArtifactIngestionDisposition.ProvisionalArtifactAdopted, CanonicalArtifactId = f.Id, AdoptionUtc = adoptionTime };
            await f.SqlAsync("""
                BEGIN IMMEDIATE;
                INSERT INTO ArtifactIngestionAdoptions VALUES($id,$op,$owner,$prior,$next,$time);
                UPDATE ArtifactMutationAuthority SET IsAdopted=1,ClassificationRevision=$next WHERE ArtifactId=$id;
                INSERT INTO Artifacts(Id,Name,ArtifactType,CreatedUtc,FingerprintAlgorithm,FingerprintValue,MetadataJson)
                    VALUES($id,'synthetic.txt','file',$time,$algorithm,$fingerprint,'{}');
                INSERT INTO Provenance(ArtifactId,Source,RecordedUtc,RecordedBy,PropertiesJson)
                    VALUES($id,$source,$time,'synthetic-bypassing-writer','{}');
                UPDATE ArtifactIngestionIntents SET State=$state,IntentJson=$json WHERE OperationId=$op;
                COMMIT;
                """, ("$id", f.Id.Value), ("$op", intent.OperationId.Value), ("$owner", intent.OwnershipToken.Value),
                ("$prior", intent.ClassificationRevision.Value), ("$next", IngestionClassificationRevision.New().Value), ("$time", adoptionTime.ToString("O")),
                ("$algorithm", f.Draft.Artifact.Fingerprint!.Algorithm), ("$fingerprint", f.Draft.Artifact.Fingerprint.Value), ("$source", f.SourcePath),
                ("$state", (int)adoptedIntent.State), ("$json", JsonSerializer.Serialize(adoptedIntent)));
            resume.SetResult();
            var outcome = await worker.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(outcome.IsAdopted); Assert.Equal(ArtifactIngestionState.RequiresReview, outcome.State);
            Assert.Equal(0, store.Deletes); Assert.NotNull(await f.Physical.ReadAsync(f.Id));
            Assert.Null(await f.Physical.GetMutationOutcomeAsync(intent.CleanupOperationId));
            Assert.Equal(claim, Assert.Single((await f.Persistence.ReadAuditObligationsAsync(intent.OperationId)).Where(x => x.Action == IngestionAuditAction.CleanupClaimed)));
            var canonical = await f.Audit.FindVerifiedAsync(new(claim.EventId.Value)); Assert.NotNull(canonical);
            Assert.Equal(claim.OccurredUtc, canonical.Record.OccurredUtc);
            await using var session = await f.Persistence.AcquireAsync(intent.OperationId);
            Assert.True(await session.HasAdoptionEvidenceAsync(f.Id));
            await Assert.ThrowsAsync<InvalidDataException>(() => session.ClaimCleanupAsync(session.Intent!,
                new(f.Id, intent.ClassificationId, intent.ClassificationRevision, false, intent.OwnershipToken, intent.AuthorizedOperationId), f.SecurityContext.RecoveryActor));
        }
        finally { resume.TrySetResult(); await worker; }
    }
    [Fact]
    public async Task Cleanup_claim_wins_race_and_stale_adopter_cannot_commit()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.StopAsync("ContentCreated");
        ArtifactIngestionIntent stale; IngestionClassificationAuthority authority;
        await using (var session = await f.Persistence.AcquireAsync(f.SecurityContext.Operation.OperationId))
        {
            stale = session.Intent!; authority = (await session.ResolveAuthorityAsync(f.Id))!;
            await session.ClaimCleanupAsync(stale, authority, f.SecurityContext.RecoveryActor); await session.CommitAsync();
        }
        await using (var session = await f.Persistence.AcquireAsync(stale.OperationId))
            await Assert.ThrowsAsync<InvalidDataException>(() => session.AdoptAsync(stale, authority, null, null));
        Assert.Equal(ArtifactIngestionState.Cleaned, (await f.Restart().RecoverOperationAsync(stale.OperationId)).State);
        Assert.Null(await f.Repository.GetArtifactAsync(f.Id));
    }
    [Theory]
    [InlineData("UPDATE ArtifactIngestionAdoptions SET OwnershipToken='changed'")]
    [InlineData("UPDATE ArtifactIngestionAdoptions SET AdoptedClassificationRevision=NULL")]
    [InlineData("DELETE FROM ArtifactIngestionAdoptions")]
    public async Task Immutable_adoption_marker_rejects_direct_mutation_clearing_and_deletion(string sql)
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.Service().IngestAsync(f.Draft, f.Content);
        await Assert.ThrowsAsync<SqliteException>(() => f.SqlAsync(sql));
        Assert.NotNull(await f.Physical.ReadAsync(f.Id));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_corrupt_intent_cannot_erase_adoption_or_authorize_deletion(bool corrupt)
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.Service().IngestAsync(f.Draft, f.Content);
        await f.SqlAsync(corrupt ? "UPDATE ArtifactIngestionIntents SET IntentJson='broken'" : "DELETE FROM ArtifactIngestionIntents");
        var result = await f.Restart().RecoverOperationAsync(f.SecurityContext.Operation.OperationId);
        Assert.True(result.IsAdopted); Assert.Equal(ArtifactIngestionState.RequiresReview, result.State);
        Assert.NotNull(await f.Physical.ReadAsync(f.Id));
        await f.Restart().ReconcileReceiptsAsync(null, 100);
        Assert.Equal(ArtifactIngestionState.RequiresReview, (await f.Restart().RecoverOperationAsync(result.OperationId)).State);
    }
    [Theory]
    [InlineData(ArtifactIngestionState.ContentCreated)]
    [InlineData(ArtifactIngestionState.CleanupClaimed)]
    public async Task Immutable_adoption_overrides_consistently_corrupted_mutable_state(ArtifactIngestionState state)
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.Service().IngestAsync(f.Draft, f.Content);
        var intent = await f.IntentAsync(); var corrupt = intent with { State = state };
        await f.SqlAsync("UPDATE ArtifactIngestionIntents SET State=$state,IntentJson=$json", ("$state", (int)state), ("$json", JsonSerializer.Serialize(corrupt)));
        var result = await f.Restart().RecoverOperationAsync(intent.OperationId);
        Assert.True(result.IsAdopted); Assert.Equal(ArtifactIngestionState.RequiresReview, result.State);
        await using var session = await f.Persistence.AcquireAsync(intent.OperationId);
        Assert.True(await session.HasAdoptionEvidenceAsync(f.Id)); Assert.NotNull(await f.Physical.ReadAsync(f.Id));
        await Assert.ThrowsAsync<InvalidDataException>(() => session.ClaimCleanupAsync(session.Intent!, new(f.Id, intent.ClassificationId, intent.ClassificationRevision, false, intent.OwnershipToken, intent.AuthorizedOperationId), f.SecurityContext.RecoveryActor));
    }
    [Fact]
    public async Task RequiresReview_never_replaces_adoption_truth_even_after_repeated_recovery()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); var original = await f.Service().IngestAsync(f.Draft, f.Content);
        await using (var session = await f.Persistence.AcquireAsync(original.OperationId))
        { await session.RecordReviewAsync("LifecycleDamage", f.SecurityContext.RecoveryActor); await session.CommitAsync(); }
        for (var i = 0; i < 3; i++)
        {
            var result = await f.Restart().RecoverOperationAsync(original.OperationId);
            Assert.True(result.IsAdopted); Assert.Equal(ArtifactIngestionState.RequiresReview, result.State);
            Assert.NotNull(await f.Physical.ReadAsync(f.Id));
        }
    }
    [Theory]
    [InlineData("DELETE FROM ArtifactMutationAuthority")]
    [InlineData("UPDATE ArtifactMutationAuthority SET ClassificationRevision='changed'")]
    [InlineData("UPDATE ArtifactMutationAuthority SET ClassificationId='Restricted'")]
    [InlineData("UPDATE ArtifactIngestionIntents SET OwnershipToken='different'")]
    [InlineData("UPDATE ArtifactIngestionIntents SET Revision=Revision+1")]
    public async Task Missing_changed_or_contradictory_authority_ownership_and_fences_require_review(string sql)
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.StopAsync("ContentCreated"); await f.SqlAsync(sql);
        var result = await f.Restart().RecoverOperationAsync(f.SecurityContext.Operation.OperationId);
        Assert.Equal(ArtifactIngestionState.RequiresReview, result.State); Assert.NotNull(await f.Physical.ReadAsync(f.Id));
    }
    [Fact]
    public async Task Reclassification_after_cleanup_claim_is_reauthorized_and_prevents_delete()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.StopAsync("ContentCreated");
        await Assert.ThrowsAsync<SimulatedCrashException>(() => f.Service(persistence: new FaultPersistence(f.Persistence, "CleanupClaimed")).RecoverOperationAsync(f.SecurityContext.Operation.OperationId));
        await f.ReclassifyAsync(f.Id, "Restricted");
        var result = await f.Restart().RecoverOperationAsync(f.SecurityContext.Operation.OperationId);
        Assert.Equal(ArtifactIngestionState.RequiresReview, result.State); Assert.NotNull(await f.Physical.ReadAsync(f.Id));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Physical_replacement_or_foreign_owner_blocks_cleanup(bool ownerChange)
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.StopAsync("ContentCreated");
        var snapshot = await f.Physical.ReadVersionedAsync(f.Id);
        if (ownerChange)
        {
            var intent = await f.IntentAsync();
            await f.Physical.DeleteIfRevisionMatchesAsync(f.Id, snapshot!.Revision, new(ArtifactContentOperationId.New(), intent.OwnershipToken));
            await f.Physical.CreateIfAbsentAsync(f.Id, "foreign synthetic content"u8.ToArray(), new(ArtifactContentOperationId.New(), new("foreign-owner")));
        }
        else await f.Physical.ReplaceIfRevisionMatchesAsync(f.Id, snapshot!.Revision, "new synthetic content"u8.ToArray(), new(ArtifactContentOperationId.New()));
        var changed = await f.Physical.ReadAsync(f.Id);
        var result = await f.Restart().RecoverOperationAsync(f.SecurityContext.Operation.OperationId);
        Assert.Equal(ArtifactIngestionState.RequiresReview, result.State); Assert.Equal(changed, await f.Physical.ReadAsync(f.Id));
    }
    [Fact]
    public async Task Missing_creation_receipt_and_cleanup_failure_remain_durable_review_work()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.StopAsync("ContentCreated");
        var hidden = new FaultStore(f.Physical) { HideCreateReceipt = true };
        var result = await f.Restart(physical: hidden).RecoverOperationAsync(f.SecurityContext.Operation.OperationId);
        Assert.Equal(ArtifactIngestionState.RequiresReview, result.State); Assert.NotNull(await f.Physical.ReadAsync(f.Id));
        Assert.NotEmpty(await f.Persistence.ReadRecoveryWorkAsync(0, 100));
    }
    [Fact]
    public async Task Cleanup_failure_is_sanitized_durable_work_and_never_success()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.StopAsync("ContentCreated");
        var failed = new FaultStore(f.Physical) { FailDelete = true };
        var result = await f.Restart(physical: failed).RecoverOperationAsync(f.SecurityContext.Operation.OperationId);
        Assert.Equal(ArtifactIngestionState.RequiresReview, result.State); Assert.Equal("CleanupOutcomeUnknown", result.SafeFailureCategory);
        Assert.NotNull(await f.Physical.ReadAsync(f.Id)); Assert.NotEmpty(await f.Persistence.ReadRecoveryWorkAsync(0, 100));
    }
    [Fact]
    public async Task Recovery_uses_service_identity_and_preserves_original_actor_in_audit()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.StopAsync("ContentCreated");
        var result = await f.Restart().RecoverOperationAsync(f.SecurityContext.Operation.OperationId);
        Assert.Equal(ArtifactIngestionState.Cleaned, result.State);
        Assert.Contains(f.Authorization.Requests, r => r.SubjectId == f.SecurityContext.RecoveryActor && r.PermissionId == SecurityPermissions.ArtifactIngestionRecover
            && r.ResourceId == f.Id.Value && r.ResourceType == SecurityResourceTypes.Artifact);
        var intent = await f.IntentAsync(); var audit = await f.Audit.FindVerifiedAsync(new(intent.CleanupEventId.Value));
        Assert.NotNull(audit); Assert.Equal(f.SecurityContext.Operation.ActorId, audit.Record.OriginalActorId);
        Assert.Equal(f.SecurityContext.RecoveryActor, audit.Record.RecoveryActorId); Assert.Equal(intent.CleanupOperationId.Value, audit.Record.OperationId!.Value.Value);
    }
    [Fact]
    public async Task Recovery_denial_does_not_delete_provisional_content()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.StopAsync("ContentCreated"); f.Authorization.AllowRecovery = false;
        var result = await f.Restart().RecoverOperationAsync(f.SecurityContext.Operation.OperationId);
        Assert.Equal(ArtifactIngestionState.RequiresReview, result.State); Assert.NotNull(await f.Physical.ReadAsync(f.Id));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unknown_or_caller_supplied_classification_never_establishes_authority(bool callerSupplied)
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        var draft = f.Draft;
        if (callerSupplied) draft = new(new Artifact { Id = f.Id, Name = "synthetic", ArtifactType = "file", Fingerprint = f.Draft.Artifact.Fingerprint,
            Metadata = new Dictionary<string, object> { ["protectionClassificationId"] = "Public" } }, f.Draft.Provenance);
        else f.Classification.Classification = null;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service().IngestAsync(draft, f.Content));
        Assert.Null(await f.Physical.ReadAsync(f.Id));
        await using var session = await f.Persistence.AcquireAsync(f.SecurityContext.Operation.OperationId); Assert.Null(session.Intent);
    }
    [Fact]
    public async Task Adoption_policy_mismatch_after_creation_requires_review()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.StopAsync("ContentCreated"); f.Classification.AdoptionAllowed = false;
        var result = await f.Restart().IngestAsync(f.Draft, f.Content);
        Assert.Equal(ArtifactIngestionState.RequiresReview, result.State); Assert.False(result.IsAdopted);
        Assert.Null(await f.Repository.GetArtifactAsync(f.Id)); Assert.NotNull(await f.Physical.ReadAsync(f.Id));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deduplication_returns_verified_canonical_and_cleans_only_provisional(bool mismatch)
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.Service().IngestAsync(f.Draft, f.Content);
        var canonicalSnapshot = await f.Physical.ReadVersionedAsync(f.Id);
        var canonicalAuthority = new EMF.Security.Persistence.Sqlite.SqliteArtifactMutationAuthority(f.DatabasePath);
        EMF.Security.Storage.ArtifactClassificationRevision canonicalRevision;
        await using (var lease = await canonicalAuthority.AcquireAsync(f.Id)) canonicalRevision = lease.ClassificationRevision;
        f.SecurityContext.Operation = new(ArtifactContentOperationId.New(), new(Guid.NewGuid().ToString("N")), "synthetic-second-actor");
        var provisional = new ArtifactId("synthetic-provisional-" + Guid.NewGuid().ToString("N"));
        var draft = new IngestionMetadataDraft(new Artifact { Id = provisional, Name = "synthetic", ArtifactType = "file", Fingerprint = f.Draft.Artifact.Fingerprint },
            new Provenance { ArtifactId = provisional, Source = f.SourcePath, RecordedBy = "synthetic" });
        if (mismatch) f.Classification.Classification = "Restricted";
        var first = await f.Service().IngestAsync(draft, f.Content);
        if (mismatch) { Assert.Equal(ArtifactIngestionDisposition.Conflict, first.Disposition); Assert.Null(first.Result); }
        else { Assert.Equal(ArtifactIngestionDisposition.DeduplicatedToCanonicalArtifact, first.Disposition); Assert.Equal(f.Id, first.Result!.Artifact.Id); }
        Assert.False(first.IsAdopted); Assert.Equal(ArtifactIngestionState.CleanupClaimed, first.State);
        var result = await f.Restart().RecoverOperationAsync(first.OperationId);
        Assert.Equal(ArtifactIngestionState.Cleaned, result.State); Assert.Null(await f.Physical.ReadAsync(provisional));
        Assert.Equal(canonicalSnapshot!.Revision, (await f.Physical.ReadVersionedAsync(f.Id))!.Revision);
        Assert.Equal(canonicalSnapshot.Content, await f.Physical.ReadAsync(f.Id));
        await using var canonicalAfter = await canonicalAuthority.AcquireAsync(f.Id); Assert.Equal(canonicalRevision, canonicalAfter.ClassificationRevision);
        if (mismatch) Assert.Equal(IngestionAuditDelivery.RequiresReview, result.AuditDelivery);
    }
    [Fact]
    public async Task Deduplication_canonical_missing_content_is_not_repaired_or_returned_as_adopted()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.Service().IngestAsync(f.Draft, f.Content);
        await f.Physical.DeleteAsync(f.Id);
        f.SecurityContext.Operation = new(ArtifactContentOperationId.New(), new(Guid.NewGuid().ToString("N")), "synthetic-second-actor");
        var id = new ArtifactId("synthetic-second");
        var draft = new IngestionMetadataDraft(new Artifact { Id = id, Name = "synthetic", ArtifactType = "file", Fingerprint = f.Draft.Artifact.Fingerprint },
            new Provenance { ArtifactId = id, Source = f.SourcePath, RecordedBy = "synthetic" });
        var result = await f.Service().IngestAsync(draft, f.Content);
        Assert.Equal(ArtifactIngestionDisposition.Conflict, result.Disposition); Assert.Null(result.Result); Assert.Null(await f.Physical.ReadAsync(f.Id));
    }
    [Fact]
    public async Task Changed_request_under_same_operation_id_is_integrity_conflict()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.Service().IngestAsync(f.Draft, f.Content);
        var other = new IngestionMetadataDraft(f.Draft.Artifact, new Provenance { ArtifactId = f.Id, Source = "synthetic-different-source", RecordedBy = "synthetic" });
        await Assert.ThrowsAsync<ArtifactContentIdempotencyException>(() => f.Restart().IngestAsync(other, f.Content));
    }
    [Fact]
    public async Task Missing_audit_row_is_restored_by_independent_receipt_reconciliation()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); var result = await f.Service(audit: new AuditOutage()).IngestAsync(f.Draft, f.Content);
        var intent = await f.IntentAsync();
        await f.SqlAsync("DELETE FROM ArtifactIngestionAudit WHERE EventId=$event", ("$event", intent.CreateEventId.Value));
        var cursor = await f.Restart().ReconcileReceiptsAsync(null, 100); Assert.NotNull(cursor);
        var recovered = await f.Restart().RecoverOperationAsync(result.OperationId); Assert.True(recovered.IsAdopted);
        Assert.NotNull(await f.Audit.FindVerifiedAsync(new(intent.CreateEventId.Value)));
        Assert.Equal(ArtifactIngestionState.Completed, recovered.State);
    }
}
