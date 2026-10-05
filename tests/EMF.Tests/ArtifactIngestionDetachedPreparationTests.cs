using EMF.Core.Contracts.Ingestion;
using EMF.Core.Contracts.Storage;
using EMF.Tests.TestInfrastructure;
using static EMF.Tests.TestInfrastructure.ArtifactIngestionFixture;

namespace EMF.Tests;

public sealed class ArtifactIngestionDetachedPreparationTests
{
    [Theory]
    [InlineData("encryption")]
    [InlineData("staging")]
    [InlineData("generation")]
    [InlineData("integrity")]
    [InlineData("catalog_integrity")]
    public async Task Blocked_preparation_releases_metadata_writer_and_preserves_other_operation_resources(string dependency)
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        await using var other = await ArtifactIngestionFixture.CreateAsync();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Block() { reached.TrySetResult(); await release.Task; }
        if (dependency == "encryption") f.Encryption.EncryptHook = Block;
        if (dependency == "integrity") f.Encryption.DecryptHook = Block;
        if (dependency == "generation") f.Physical.Checkpoint = point => point == "CandidateDurable" ? Block() : Task.CompletedTask;
        if (dependency == "catalog_integrity") f.Physical.Checkpoint = point => point == "CatalogIntegrityPrepared" ? Block() : Task.CompletedTask;
        IArtifactContentStagingStore? staging = dependency == "staging" ? new CheckpointStaging(f.Staging, Block) : null;
        var ingestion = Task.Run(() => f.Service(staging: staging).IngestAsync(f.Draft, f.Content));
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // Actual independent connection and write to the SAME metadata database.
            await Task.Run(() => f.SqlAsync("CREATE TABLE UnrelatedWriter(Value INTEGER); INSERT INTO UnrelatedWriter VALUES(1)"))
                .WaitAsync(TimeSpan.FromSeconds(3));
            await using (var session = await f.Persistence.AcquireAsync(ArtifactContentOperationId.New()))
            { Assert.Null(session.Intent); await session.CommitAsync(); }
            var unrelated = await other.Service().IngestAsync(other.Draft, other.Content);
            Assert.True(unrelated.IsAdopted);
            Assert.False(ingestion.IsCompleted);
        }
        finally { release.TrySetResult(); }
        var result = await ingestion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result.IsAdopted);
        Assert.NotNull(await other.Repository.GetArtifactAsync(other.Id));
    }

    [Fact]
    public async Task Reclassification_during_detached_encryption_fails_closed_without_physical_promotion()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        f.Encryption.EncryptHook = () => f.ReclassifyAsync(f.Id, "Confidential"); // New revision, same label.
        var result = await f.Service().IngestAsync(f.Draft, f.Content);
        Assert.Equal(ArtifactIngestionState.RequiresReview, result.State);
        Assert.Equal("ProvisionalAuthorityFailure", result.SafeFailureCategory);
        Assert.False(result.IsAdopted);
        Assert.Null(await f.Physical.GetMutationOutcomeAsync(f.SecurityContext.Operation.OperationId));
        Assert.NotNull(await f.Staging.ReadAsync(f.SecurityContext.Operation.OperationId));
    }

    [Fact]
    public async Task Detached_creator_excludes_recovery_and_same_operation_retry_without_blocking_database()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Encryption.EncryptHook = async () => { reached.TrySetResult(); await release.Task; };
        var original = Task.Run(() => f.Service().IngestAsync(f.Draft, f.Content));
        Task<ArtifactIngestionOutcome>? recovery = null;
        Task<ArtifactIngestionOutcome>? replay = null;
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            recovery = Task.Run(() => f.Restart().RecoverOperationAsync(f.SecurityContext.Operation.OperationId));
            replay = Task.Run(() => f.Restart().IngestAsync(f.Draft, f.Content));
            await Task.Run(() => f.SqlAsync("CREATE TABLE RaceWriter(Value INTEGER); INSERT INTO RaceWriter VALUES(1)"))
                .WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(recovery.IsCompleted); Assert.False(replay.IsCompleted);
        }
        finally { release.TrySetResult(); }
        Assert.True((await original.WaitAsync(TimeSpan.FromSeconds(10))).IsAdopted);
        Assert.True((await recovery!.WaitAsync(TimeSpan.FromSeconds(10))).IsAdopted);
        Assert.True((await replay!.WaitAsync(TimeSpan.FromSeconds(10))).IsAdopted);
        Assert.Equal(1, f.Encryption.Encryptions);
        Assert.Single(await f.Physical.ReadAuditObligationsAsync(null, 100));
    }

    [Fact]
    public async Task Lost_encryption_candidate_is_not_regenerated_even_if_original_plaintext_is_supplied()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        f.Encryption.EncryptHook = () => throw new SimulatedCrashException();
        await Assert.ThrowsAsync<SimulatedCrashException>(() => f.Service().IngestAsync(f.Draft, f.Content));
        f.Encryption.EncryptHook = null;
        var result = await f.Restart().IngestAsync(f.Draft, f.Content);
        Assert.Equal(ArtifactIngestionState.Cleaned, result.State);
        Assert.Equal(1, f.Encryption.Encryptions);
        Assert.Null(await f.Physical.ReadAsync(f.Id));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Canonical_integrity_runs_detached_and_stale_revision_cannot_be_deduplicated(bool replace)
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        var adopted = await f.Service().IngestAsync(f.Draft, f.Content);
        var canonicalId = f.Id;
        var canonicalSnapshot = await f.Physical.ReadVersionedAsync(canonicalId);
        var operation = f.SecurityContext.Operation;
        f.SecurityContext.Operation = operation with { OperationId = ArtifactContentOperationId.New(), AuthorizedOperationId = new(Guid.NewGuid().ToString("N")) };
        var newId = new EMF.Core.Models.Identities.ArtifactId("detached-canonical-" + Guid.NewGuid().ToString("N"));
        var draft = new IngestionMetadataDraft(new EMF.Core.Models.Artifact
        {
            Id = newId, Name = f.Draft.Artifact.Name, ArtifactType = f.Draft.Artifact.ArtifactType,
            Fingerprint = f.Draft.Artifact.Fingerprint
        }, new EMF.Core.Models.Provenance { ArtifactId = newId, Source = f.Draft.Provenance.Source, RecordedBy = "synthetic-detached-test" });
        var decryptions = 0;
        f.Encryption.DecryptHook = async () =>
        {
            if (++decryptions != 2) return; // First verifies new staged request; second verifies canonical bytes.
            await Task.Run(() => f.SqlAsync("CREATE TABLE IntegrityWriter(Value INTEGER); INSERT INTO IntegrityWriter VALUES(1)"))
                .WaitAsync(TimeSpan.FromSeconds(3));
            if (replace)
                await f.Physical.ReplaceIfRevisionMatchesAsync(canonicalId, canonicalSnapshot!.Revision,
                    canonicalSnapshot.Content, new(ArtifactContentOperationId.New()));
        };
        var result = await f.Service().IngestAsync(draft, f.Content);
        Assert.Equal(2, decryptions);
        if (replace)
        {
            Assert.Equal(ArtifactIngestionDisposition.Conflict, result.Disposition);
            Assert.Null(result.Result);
            Assert.NotEqual(canonicalSnapshot!.Revision, (await f.Physical.ReadVersionedAsync(canonicalId))!.Revision);
        }
        else
        {
            Assert.Equal(ArtifactIngestionDisposition.DeduplicatedToCanonicalArtifact, result.Disposition);
            Assert.Equal(canonicalId, result.Result!.Artifact.Id);
        }
        Assert.NotNull(await f.Repository.GetArtifactAsync(canonicalId));
    }

    [Fact]
    public async Task Version_one_migration_preserves_bindings_and_conservatively_prevents_reencryption()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        await f.StopAsync("Prepared");
        var original = await f.IntentAsync();
        await f.SqlAsync("DROP TRIGGER ArtifactIngestionCandidatePreparationNoUpdate; DROP TRIGGER ArtifactIngestionCandidatePreparationNoDelete; DROP TRIGGER ArtifactIngestionCandidatePreparationNoReplace; DROP TABLE ArtifactIngestionCandidatePreparations; DROP TRIGGER ArtifactIngestionCandidateNoUpdate; DROP TRIGGER ArtifactIngestionCandidateNoDelete; DROP TRIGGER ArtifactIngestionCandidateNoReplace; DROP TABLE ArtifactIngestionCandidates; DROP TRIGGER ArtifactIngestionCandidateCreationNoUpdate; DROP TRIGGER ArtifactIngestionCandidateCreationNoDelete; DROP TRIGGER ArtifactIngestionCandidateCreationNoReplace; DROP TABLE ArtifactIngestionCandidateCreations; DELETE FROM ArtifactIngestionSchema; INSERT INTO ArtifactIngestionSchema VALUES(1)");
        await f.Persistence.InitializeAsync();
        Assert.Equal(original, await f.IntentAsync());
        await using (var session = await f.Persistence.AcquireAsync(original.OperationId))
            Assert.True(session.CandidatePreparationStarted);
        var result = await f.Restart().IngestAsync(f.Draft, f.Content);
        Assert.Equal(ArtifactIngestionState.Cleaned, result.State);
        Assert.Equal(0, f.Encryption.Encryptions);
    }

    [Fact]
    public async Task Candidate_preparation_admission_is_immutable_and_cannot_be_rewound()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        await Assert.ThrowsAsync<SimulatedCrashException>(() => f.Service(persistence: new FaultPersistence(f.Persistence, "PreparationStarted")).IngestAsync(f.Draft, f.Content));
        foreach (var sql in new[]
        {
            "DELETE FROM ArtifactIngestionCandidatePreparations",
            "UPDATE ArtifactIngestionCandidatePreparations SET ExpectedRevision=2",
            "INSERT OR REPLACE INTO ArtifactIngestionCandidatePreparations SELECT * FROM ArtifactIngestionCandidatePreparations"
        })
            await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => f.SqlAsync(sql));
    }

    [Fact]
    public async Task Candidate_binding_rejects_changed_payload_and_request_and_is_not_lifecycle_authority()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.StopAsync("Candidate");
        var intent = await f.IntentAsync();
        var retained = await f.Staging.ReadAsync(intent.OperationId);
        await Assert.ThrowsAsync<ArtifactContentIdempotencyException>(() => f.Staging.StageAsync(intent.OperationId, new byte[] { 1 }));
        await using (var session = await f.Persistence.AcquireAsync(intent.OperationId))
        {
            await Assert.ThrowsAsync<ArtifactContentIdempotencyException>(() => session.SetCandidateAsync(intent, new string('A', 64)));
            Assert.False(await session.HasAdoptionEvidenceAsync(f.Id));
        }
        foreach (var sql in new[] { "DELETE FROM ArtifactIngestionCandidates", "UPDATE ArtifactIngestionCandidates SET CandidateHash='changed'", "INSERT OR REPLACE INTO ArtifactIngestionCandidates SELECT * FROM ArtifactIngestionCandidates" })
            await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => f.SqlAsync(sql));
        await f.SqlAsync("UPDATE ArtifactIngestionDrafts SET DraftJson=json_set(DraftJson,'$.Artifact.Name','changed')");
        var result = await f.Restart().RecoverOperationAsync(intent.OperationId);
        Assert.Equal(ArtifactIngestionState.RequiresReview, result.State);
        Assert.Equal(retained, await f.Staging.ReadAsync(intent.OperationId));
        Assert.Null(await f.Physical.ReadAsync(f.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Admitted_candidate_is_preserved_and_classification_is_never_silently_rebound(bool reclassify)
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        f.Physical.Checkpoint = async point =>
        {
            if (point == "CandidateDurable" && reclassify) await f.ReclassifyAsync(f.Id, "Restricted");
        };
        var result = await f.Service().IngestAsync(f.Draft, f.Content);
        var intent = await f.IntentAsync();
        var candidate = await f.Staging.ReadAsync(intent.OperationId);
        Assert.NotNull(intent.CandidateHash); Assert.NotNull(candidate);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(candidate)), intent.CandidateHash);
        Assert.Equal("Confidential", intent.ClassificationId.Value);
        Assert.Equal(1, f.Encryption.Encryptions);
        if (reclassify)
        {
            Assert.Equal(ArtifactIngestionState.RequiresReview, result.State);
            Assert.False(result.IsAdopted); Assert.Null(await f.Physical.GetMutationOutcomeAsync(intent.OperationId));
        }
        else
        {
            Assert.True(result.IsAdopted); Assert.Equal(candidate, await f.Physical.ReadAsync(f.Id));
        }
    }

    [Fact]
    public async Task Immutable_candidate_binds_authenticated_parent_child_authority_contract_audit_and_creation_receipt()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        var parent = ArtifactContentOperationId.New();
        f.SecurityContext.Operation = f.SecurityContext.Operation with { ParentOperationId = parent };
        var result = await f.Service().IngestAsync(f.Draft, f.Content);
        Assert.True(result.IsAdopted);
        await using (var session = await f.Persistence.AcquireAsync(result.OperationId))
        {
            var candidate = Assert.IsType<IngestionCandidateBinding>(session.CandidateBinding);
            var intent = session.Intent!;
            Assert.Equal(parent, candidate.ParentOperationId); Assert.Equal(result.OperationId, candidate.OperationId);
            Assert.Equal(f.Id, candidate.ArtifactId); Assert.Equal(intent.OwnershipToken, candidate.OwnershipToken);
            Assert.Equal(intent.ClassificationRevision, candidate.ClassificationRevision);
            Assert.Equal(intent.AuthorizedOperationId, candidate.AuthorizedOperationId);
            Assert.Equal(intent.CreateEventId, candidate.CreateEventId); Assert.Equal(intent.AdoptionEventId, candidate.AdoptionEventId);
            Assert.Equal(intent.CleanupEventId, candidate.CleanupEventId);
            Assert.Equal("ArtifactEnvelope.ContextBound.v2", candidate.ProtectionContract);
            Assert.Equal(intent.CreateReceipt, session.CandidateCreationReceipt);
            Assert.Equal(candidate.CreateEventId, session.CandidateCreationReceipt!.AuditEventId);
            Assert.NotNull(session.CandidateCreationReceipt.CurrentRevision);
            Assert.True(await session.HasAdoptionEvidenceAsync(f.Id));
        }
        foreach (var sql in new[] { "DELETE FROM ArtifactIngestionCandidateCreations", "UPDATE ArtifactIngestionCandidateCreations SET ReceiptJson='changed'", "INSERT OR REPLACE INTO ArtifactIngestionCandidateCreations SELECT * FROM ArtifactIngestionCandidateCreations" })
            await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => f.SqlAsync(sql));
        f.SecurityContext.Operation = f.SecurityContext.Operation with { ParentOperationId = ArtifactContentOperationId.New() };
        await Assert.ThrowsAsync<ArtifactContentIdempotencyException>(() => f.Service().IngestAsync(f.Draft, f.Content));
    }

    [Fact]
    public async Task Changed_request_under_same_operation_cannot_be_silently_ignored_or_rebound()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.StopAsync("Candidate");
        var bytes = await f.Staging.ReadAsync(f.SecurityContext.Operation.OperationId);
        var changed = new IngestionMetadataDraft(new EMF.Core.Models.Artifact
        {
            Id = f.Id, Name = "changed-request", ArtifactType = f.Draft.Artifact.ArtifactType,
            CreatedUtc = f.Draft.Artifact.CreatedUtc, Fingerprint = f.Draft.Artifact.Fingerprint, Metadata = f.Draft.Artifact.Metadata
        }, f.Draft.Provenance);
        await Assert.ThrowsAsync<ArtifactContentIdempotencyException>(() => f.Service().IngestAsync(changed, f.Content));
        Assert.Equal(bytes, await f.Staging.ReadAsync(f.SecurityContext.Operation.OperationId));
        Assert.Null(await f.Physical.ReadAsync(f.Id));
        var result = await f.Service().ResumeAsync();
        Assert.True(result.IsAdopted);
        Assert.Equal("synthetic.txt", result.Result!.Artifact.Name);
        Assert.Equal(1, f.Encryption.Encryptions);
    }

    [Fact]
    public async Task Changed_payload_under_same_operation_is_integrity_conflict_without_reencryption()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.StopAsync("Candidate");
        var bytes = await f.Staging.ReadAsync(f.SecurityContext.Operation.OperationId);
        var changedBytes = "different synthetic payload"u8.ToArray();
        var changed = new IngestionMetadataDraft(new EMF.Core.Models.Artifact
        {
            Id = f.Id, Name = f.Draft.Artifact.Name, ArtifactType = f.Draft.Artifact.ArtifactType,
            CreatedUtc = f.Draft.Artifact.CreatedUtc, Fingerprint = await f.Fingerprints.ComputeAsync(changedBytes), Metadata = f.Draft.Artifact.Metadata
        }, f.Draft.Provenance);
        await Assert.ThrowsAsync<ArtifactContentIdempotencyException>(() => f.Service().IngestAsync(changed, changedBytes));
        Assert.Equal(bytes, await f.Staging.ReadAsync(f.SecurityContext.Operation.OperationId));
        Assert.Equal(1, f.Encryption.Encryptions);
        Assert.Null(await f.Physical.GetMutationOutcomeAsync(f.SecurityContext.Operation.OperationId));
        Assert.True((await f.Service().IngestAsync(f.Draft, f.Content)).IsAdopted);
        Assert.Equal(1, f.Encryption.Encryptions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_corrupt_retained_draft_requires_review_without_source_reconstruction(bool corrupt)
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.StopAsync("Candidate");
        var bytes = await f.Staging.ReadAsync(f.SecurityContext.Operation.OperationId);
        File.Delete(f.SourcePath);
        await f.SqlAsync(corrupt ? "UPDATE ArtifactIngestionDrafts SET DraftJson='broken'" : "DELETE FROM ArtifactIngestionDrafts");
        await Assert.ThrowsAsync<ArtifactIngestionReviewException>(() => f.Service().ResumeAsync());
        var result = await f.Restart().FinishAsync(f.SecurityContext.Operation.OperationId);
        Assert.Equal(ArtifactIngestionState.RequiresReview, result.State);
        Assert.False(result.IsAdopted);
        Assert.Equal(bytes, await f.Staging.ReadAsync(f.SecurityContext.Operation.OperationId));
        Assert.Equal(1, f.Encryption.Encryptions);
        Assert.Null(await f.Physical.GetMutationOutcomeAsync(f.SecurityContext.Operation.OperationId));
    }

}
