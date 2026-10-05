using System.Diagnostics;
using System.Text.Json;
using EMF.Core.Contracts.Ingestion;
using EMF.Core.Models.Identities;
using EMF.Security.Ingestion;
using EMF.Tests.TestInfrastructure;
using static EMF.Tests.TestInfrastructure.ArtifactIngestionFixture;

namespace EMF.Tests;

public sealed class ArtifactIngestionProcessRecoveryTests
{
    private sealed record WorkerRequest(ArtifactId ArtifactId, AuthenticatedIngestionOperation Operation,
        string Checkpoint, bool BeforeCommit, bool Cleanup, bool RecoverOnly = false, bool Resume = false, bool PauseReview = false);
    private sealed record WorkerRecoveryResult(ArtifactIngestionState State, bool IsAdopted, int Encryptions = 0, bool ReviewAuthorizationReacquired = false);
    [Theory]
    [InlineData("Prepared", false, false, false, false)]
    [InlineData("PreparationStarted", false, false, false, false)]
    [InlineData("Encrypting", false, false, false, false)]
    [InlineData("Staged", false, false, false, false)]
    [InlineData("GenerationPrepared", false, false, false, false)]
    [InlineData("Candidate", false, false, false, false)]
    [InlineData("ContentCreated", false, false, false, false)]
    [InlineData("PhysicalCreated", false, false, false, false)]
    [InlineData("MetadataCommitted", true, false, false, false)]
    [InlineData("MetadataCommitted", false, false, false, false)]
    [InlineData("Completed", false, false, false, false)]
    [InlineData("CleanupClaimed", false, true, false, false)]
    [InlineData("Cleaned", false, true, false, false)]
    [InlineData("PhysicalDeleted", false, true, false, false)]
    [InlineData("Candidate", false, false, true, false)]
    [InlineData("GenerationPrepared", false, false, true, false)]
    [InlineData("PhysicalCreated", false, false, true, false)]
    [InlineData("GenerationPrepared", false, false, true, true)]
    public async Task Killed_worker_releases_metadata_fence_and_recovery_honors_committed_state(string checkpoint, bool beforeCommit, bool cleanup, bool resume, bool reclassify)
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        if (cleanup) await f.StopAsync("ContentCreated");
        await File.WriteAllTextAsync(Path.Combine(f.Root, "worker-allowed"), "EMF-M5-SYNTHETIC-PROCESS-WORKER");
        await File.WriteAllTextAsync(Path.Combine(f.Root, "worker-request.json"), JsonSerializer.Serialize(new WorkerRequest(f.Id, f.SecurityContext.Operation, checkpoint, beforeCommit, cleanup)));
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("vstest"); info.ArgumentList.Add(typeof(ArtifactIngestionProcessRecoveryTests).Assembly.Location);
        info.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=EMF.Tests.ArtifactIngestionProcessRecoveryTests.SyntheticProcessWorker");
        info.Environment["EMF_M5_SYNTHETIC_WORKER_ROOT"] = f.Root;
        info.Environment["EMF_AZURE_OPENAI_LIVE_TESTS"] = "false"; info.Environment["EMF_AZURE_OPENAI_LIVE"] = "false";
        info.Environment["EMF_AZURE_MONITOR_LIVE_TESTS"] = "false";
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        try
        {
            var timer = Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(f.Root, "worker-ready")))
            {
                if (process.HasExited) throw new InvalidOperationException("Synthetic worker exited early: " + await output + await error);
                if (timer.Elapsed > TimeSpan.FromSeconds(25)) throw new TimeoutException("Synthetic ingestion worker did not reach checkpoint.");
                await Task.Delay(25);
            }
            process.Kill(entireProcessTree: true); await process.WaitForExitAsync();
            var committedClaim = checkpoint == "CleanupClaimed" && !beforeCommit
                ? Assert.Single((await f.Persistence.ReadAuditObligationsAsync(f.SecurityContext.Operation.OperationId))
                    .Where(x => x.Action == IngestionAuditAction.CleanupClaimed)) : null;
            if (committedClaim is not null)
                await f.SqlAsync("DELETE FROM ArtifactIngestionAudit WHERE EventId=$event", ("$event", committedClaim.EventId.Value));
            var retainedCandidate = await f.Staging.ReadAsync(f.SecurityContext.Operation.OperationId);
            var admittedIntent = await f.IntentAsync();
            IngestionCandidateBinding? admittedBinding;
            await using (var session = await f.Persistence.AcquireAsync(f.SecurityContext.Operation.OperationId)) admittedBinding = session.CandidateBinding;
            if (reclassify) await f.ReclassifyAsync(f.Id, "Restricted");
            File.Delete(f.SourcePath);
            await File.WriteAllTextAsync(Path.Combine(f.Root, "worker-request.json"), JsonSerializer.Serialize(new WorkerRequest(f.Id, f.SecurityContext.Operation, checkpoint, beforeCommit, cleanup, true, resume)));
            using var recoveryProcess = Process.Start(info)!;
            var recoveryOutput = recoveryProcess.StandardOutput.ReadToEndAsync(); var recoveryError = recoveryProcess.StandardError.ReadToEndAsync();
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                await recoveryProcess.WaitForExitAsync(deadline.Token);
                Assert.Equal(0, recoveryProcess.ExitCode);
            }
            finally
            {
                if (!recoveryProcess.HasExited) { recoveryProcess.Kill(true); await recoveryProcess.WaitForExitAsync(); }
                await recoveryOutput; await recoveryError;
            }
            var recovered = JsonSerializer.Deserialize<WorkerRecoveryResult>(await File.ReadAllTextAsync(Path.Combine(f.Root, "worker-recovered.json")))!;
            if (committedClaim is not null)
            {
                await RunFreshRecoveryProcessAsync(f.Root);
                var finalClaim = Assert.Single((await f.Persistence.ReadAuditObligationsAsync(f.SecurityContext.Operation.OperationId))
                    .Where(x => x.Action == IngestionAuditAction.CleanupClaimed));
                Assert.Equal(committedClaim, finalClaim);
                var canonical = await f.Audit.FindVerifiedAsync(new(finalClaim.EventId.Value)); Assert.NotNull(canonical);
                Assert.Equal(committedClaim.OccurredUtc, canonical.Record.OccurredUtc);
                Assert.Equal(committedClaim.ExecutingActorId, canonical.Record.RecoveryActorId);
                Assert.Equal(3, (await new EMF.Security.Persistence.Sqlite.Auditing.SqliteSecurityAuditIntegrityVerifier(f.AuditPath).VerifyAsync()).ProtectedRecordCount);
            }
            var repeated = await f.Restart().RecoverOperationAsync(f.SecurityContext.Operation.OperationId);
            var adopted = resume && !reclassify || !beforeCommit && checkpoint is "MetadataCommitted" or "Completed";
            if (resume)
            {
                Assert.Equal(0, recovered.Encryptions);
                Assert.NotNull(retainedCandidate);
                Assert.Equal(retainedCandidate, await f.Staging.ReadAsync(f.SecurityContext.Operation.OperationId));
                if (!reclassify) Assert.Equal(retainedCandidate, await f.Physical.ReadAsync(f.Id));
                await using (var session = await f.Persistence.AcquireAsync(f.SecurityContext.Operation.OperationId)) Assert.Equal(admittedBinding, session.CandidateBinding);
                Assert.Equal(admittedIntent.CandidateHash, (await f.IntentAsync()).CandidateHash);
                if (reclassify) Assert.Empty(await f.Physical.ReadAuditObligationsAsync(null, 100));
                else Assert.Single(await f.Physical.ReadAuditObligationsAsync(null, 100));
            }
            Assert.Equal(adopted, recovered.IsAdopted); Assert.Equal(adopted, repeated.IsAdopted);
            Assert.Equal(reclassify ? ArtifactIngestionState.RequiresReview : adopted ? ArtifactIngestionState.Completed : ArtifactIngestionState.Cleaned, repeated.State);
            if (adopted) Assert.NotNull(await f.Physical.ReadAsync(f.Id)); else Assert.Null(await f.Physical.ReadAsync(f.Id));
        }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } await output; await error; }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Review_event_identity_survives_A_B_loss_again_C_without_duplicate_or_cleanup(bool corrupt)
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.Service().IngestAsync(f.Draft, f.Content);
        var intent = await f.IntentAsync(); var physical = await f.Physical.ReadVersionedAsync(f.Id);
        var marker = await AdoptionSnapshotAsync(f.DatabasePath);
        await File.WriteAllTextAsync(Path.Combine(f.Root, "worker-allowed"), "EMF-M5-SYNTHETIC-PROCESS-WORKER");
        await File.WriteAllTextAsync(Path.Combine(f.Root, "worker-request.json"), JsonSerializer.Serialize(new WorkerRequest(f.Id, f.SecurityContext.Operation, "review", false, false, true)));
        await f.SqlAsync(corrupt ? "UPDATE ArtifactIngestionIntents SET IntentJson='broken'" : "DELETE FROM ArtifactIngestionIntents");
        await RunFreshRecoveryProcessAsync(f.Root); // A
        var first = Assert.Single((await f.Persistence.ReadAuditObligationsAsync(intent.OperationId)).Where(x => x.Action == IngestionAuditAction.RequiresReview));
        var canonical = await f.Audit.FindVerifiedAsync(new(first.EventId.Value)); Assert.NotNull(canonical);
        var bytes = EMF.Security.Auditing.SecurityAuditCanonicalEvent.Encode(canonical.Record);
        Assert.Equal("LifecycleDamage", canonical.Record.Facts["recoveryCondition"]);
        Assert.Equal("1", canonical.Record.Facts["ingestionSchemaVersion"]);
        await RunFreshRecoveryProcessAsync(f.Root); // B
        await f.SqlAsync("DELETE FROM ArtifactIngestionIntents; DELETE FROM ArtifactIngestionAudit WHERE EventId=$event", ("$event", first.EventId.Value));
        await RunFreshRecoveryProcessAsync(f.Root); // C
        var final = Assert.Single((await f.Persistence.ReadAuditObligationsAsync(intent.OperationId)).Where(x => x.Action == IngestionAuditAction.RequiresReview));
        Assert.Equal(first, final);
        var verified = await f.Audit.FindVerifiedAsync(new(final.EventId.Value)); Assert.NotNull(verified);
        Assert.Equal(bytes, EMF.Security.Auditing.SecurityAuditCanonicalEvent.Encode(verified.Record));
        Assert.Equal(marker, await AdoptionSnapshotAsync(f.DatabasePath));
        Assert.Equal(physical!.Revision, (await f.Physical.ReadVersionedAsync(f.Id))!.Revision);
        Assert.Equal(physical.Content, await f.Physical.ReadAsync(f.Id));
        Assert.Null(await f.Physical.GetMutationOutcomeAsync(intent.CleanupOperationId));
        Assert.Single(await f.Physical.ReadAuditObligationsAsync(null, 100));
        Assert.DoesNotContain(await f.Persistence.ReadAuditObligationsAsync(intent.OperationId), x => x.Action is IngestionAuditAction.CleanupClaimed or IngestionAuditAction.Deleted);
        Assert.Equal(3, (await new EMF.Security.Persistence.Sqlite.Auditing.SqliteSecurityAuditIntegrityVerifier(f.AuditPath).VerifyAsync()).ProtectedRecordCount);
    }
    private static async Task<string> AdoptionSnapshotAsync(string databasePath)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString());
        await connection.OpenAsync(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT ArtifactId,OperationId,OwnershipToken,ProvisionalClassificationRevision,AdoptedClassificationRevision,AdoptedUtc FROM ArtifactIngestionAdoptions";
        using var reader = await command.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
        return JsonSerializer.Serialize(Enumerable.Range(0, 6).Select(reader.GetString).ToArray());
    }
    [Fact]
    public async Task Corrupt_receipt_detection_survives_process_death_before_authorized_review_commit()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync(); await f.StopAsync("ContentCreated");
        var intent = await f.IntentAsync();
        var catalog = Path.Combine(f.Root, "content", ".content-catalog.sqlite");
        var generation = Assert.Single(Directory.GetFiles(Path.Combine(f.Root, "content", ".content-generations")));
        var generationBytes = await File.ReadAllBytesAsync(generation);
        var candidateBytes = await f.Staging.ReadAsync(intent.OperationId);
        IngestionCandidateBinding? admitted;
        await using (var session = await f.Persistence.AcquireAsync(intent.OperationId)) admitted = session.CandidateBinding;
        async Task<string> ReceiptJson()
        {
            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = catalog, Pooling = false }.ToString());
            await connection.OpenAsync(); using var command = connection.CreateCommand();
            command.CommandText = "SELECT Receipt FROM ContentReceipts WHERE OperationId=$op";
            command.Parameters.AddWithValue("$op", intent.OperationId.Value);
            return (string)(await command.ExecuteScalarAsync())!;
        }
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = catalog, Pooling = false }.ToString()))
        {
            await connection.OpenAsync(); using var command = connection.CreateCommand();
            command.CommandText = "UPDATE ContentReceipts SET Receipt=json_set(Receipt,'$.OwnershipToken.Value','foreign-owner')";
            await command.ExecuteNonQueryAsync();
        }
        var corrupted = await ReceiptJson();
        File.Delete(f.SourcePath);
        await File.WriteAllTextAsync(Path.Combine(f.Root, "worker-allowed"), "EMF-M5-SYNTHETIC-PROCESS-WORKER");
        await File.WriteAllTextAsync(Path.Combine(f.Root, "worker-request.json"), JsonSerializer.Serialize(new WorkerRequest(f.Id, f.SecurityContext.Operation,
            "RequiresReview", true, false, RecoverOnly: true, PauseReview: true)));
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("vstest"); info.ArgumentList.Add(typeof(ArtifactIngestionProcessRecoveryTests).Assembly.Location);
        info.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=EMF.Tests.ArtifactIngestionProcessRecoveryTests.SyntheticProcessWorker");
        info.Environment["EMF_M5_SYNTHETIC_WORKER_ROOT"] = f.Root;
        info.Environment["EMF_AZURE_OPENAI_LIVE_TESTS"] = "false"; info.Environment["EMF_AZURE_OPENAI_LIVE"] = "false";
        info.Environment["EMF_AZURE_MONITOR_LIVE_TESTS"] = "false";
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            while (!File.Exists(Path.Combine(f.Root, "worker-ready")))
            {
                if (process.HasExited) throw new InvalidOperationException(await output + await error);
                await Task.Delay(25, deadline.Token);
            }
            process.Kill(true); await process.WaitForExitAsync();
            Assert.Equal(ArtifactIngestionState.ContentCreated, (await f.IntentAsync()).State);
            Assert.Null(await f.Persistence.ReadReviewAuditObligationAsync(intent.OperationId));
            await File.WriteAllTextAsync(Path.Combine(f.Root, "worker-request.json"), JsonSerializer.Serialize(new WorkerRequest(f.Id, f.SecurityContext.Operation,
                "RequiresReview", false, false, RecoverOnly: true)));
            await RunFreshRecoveryProcessAsync(f.Root);
            Assert.Equal(ArtifactIngestionState.RequiresReview, (await f.IntentAsync()).State);
            var resumed = JsonSerializer.Deserialize<WorkerRecoveryResult>(await File.ReadAllTextAsync(Path.Combine(f.Root, "worker-recovered.json")))!;
            Assert.True(resumed.ReviewAuthorizationReacquired); Assert.Equal(0, resumed.Encryptions);
            var first = await f.Persistence.ReadReviewAuditObligationAsync(intent.OperationId);
            Assert.NotNull(first); Assert.True(first.IsRecovery);
            Assert.Equal(f.SecurityContext.RecoveryActor, first.ExecutingActorId);
            Assert.Equal("RecoveryEvidenceFailure", first.SafeFailureCategory);
            var canonical = await f.Audit.FindVerifiedAsync(new(first.EventId.Value)); Assert.NotNull(canonical);
            var canonicalBytes = EMF.Security.Auditing.SecurityAuditCanonicalEvent.Encode(canonical.Record);
            Assert.Equal(first.OccurredUtc, canonical.Record.OccurredUtc);
            Assert.Equal("RecoveryEvidenceFailure", canonical.Record.Facts["recoveryCondition"]);
            Assert.Equal("RequiresReview", canonical.Record.Facts["disposition"]);
            await RunFreshRecoveryProcessAsync(f.Root);
            Assert.Equal(first, await f.Persistence.ReadReviewAuditObligationAsync(intent.OperationId));
            var repeated = JsonSerializer.Deserialize<WorkerRecoveryResult>(await File.ReadAllTextAsync(Path.Combine(f.Root, "worker-recovered.json")))!;
            Assert.True(repeated.ReviewAuthorizationReacquired); Assert.Equal(0, repeated.Encryptions);
            var finalCanonical = await f.Audit.FindVerifiedAsync(new(first.EventId.Value)); Assert.NotNull(finalCanonical);
            Assert.Equal(canonicalBytes, EMF.Security.Auditing.SecurityAuditCanonicalEvent.Encode(finalCanonical.Record));
            Assert.Equal(canonical.Record.OccurredUtc, finalCanonical.Record.OccurredUtc);
            Assert.Equal("RecoveryEvidenceFailure", finalCanonical.Record.Facts["recoveryCondition"]);
            Assert.Equal(1, (await new EMF.Security.Persistence.Sqlite.Auditing.SqliteSecurityAuditIntegrityVerifier(f.AuditPath).VerifyAsync()).ProtectedRecordCount);
            Assert.Equal(corrupted, await ReceiptJson());
            Assert.Null(await f.Repository.GetArtifactAsync(f.Id));
            Assert.Equal(generation, Assert.Single(Directory.GetFiles(Path.Combine(f.Root, "content", ".content-generations"))));
            Assert.Equal(generationBytes, await File.ReadAllBytesAsync(generation));
            Assert.Equal(candidateBytes, await f.Staging.ReadAsync(intent.OperationId));
            Assert.Single(Directory.GetFiles(Path.Combine(f.Root, "staging"), "*.candidate"));
            Assert.False(File.Exists(f.SourcePath));
            await using (var session = await f.Persistence.AcquireAsync(intent.OperationId))
            {
                Assert.False(await session.HasAdoptionEvidenceAsync(f.Id));
                Assert.Equal(ArtifactIngestionState.RequiresReview, session.Intent!.State);
                Assert.Equal(ArtifactIngestionDisposition.None, session.Intent.Disposition);
                Assert.Null(session.Intent.CleanupReceipt); Assert.Null(session.Intent.CleanupActorId);
                Assert.Equal(admitted, session.CandidateBinding);
                Assert.Equal(intent.CreateReceipt, session.CandidateCreationReceipt);
            }
            var obligations = await f.Persistence.ReadAuditObligationsAsync(intent.OperationId);
            Assert.Single(obligations.Where(x => x.Action == IngestionAuditAction.RequiresReview));
            Assert.DoesNotContain(obligations, x => x.Action is IngestionAuditAction.CleanupClaimed or IngestionAuditAction.Deleted or IngestionAuditAction.Adopted);
            await using var check = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = catalog, Pooling = false }.ToString());
            await check.OpenAsync(); using var count = check.CreateCommand(); count.CommandText = "SELECT COUNT(*) FROM ContentReceipts";
            Assert.Equal(1L, await count.ExecuteScalarAsync());
        }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } await output; await error; }
    }

    private static async Task RunFreshRecoveryProcessAsync(string root)
    {
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("vstest"); info.ArgumentList.Add(typeof(ArtifactIngestionProcessRecoveryTests).Assembly.Location);
        info.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=EMF.Tests.ArtifactIngestionProcessRecoveryTests.SyntheticProcessWorker");
        info.Environment["EMF_M5_SYNTHETIC_WORKER_ROOT"] = root;
        info.Environment["EMF_AZURE_OPENAI_LIVE_TESTS"] = "false"; info.Environment["EMF_AZURE_OPENAI_LIVE"] = "false";
        info.Environment["EMF_AZURE_MONITOR_LIVE_TESTS"] = "false";
        using var process = Process.Start(info)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25)); await process.WaitForExitAsync(deadline.Token);
            Assert.True(process.ExitCode == 0, await output + await error);
        }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } await output; await error; }
    }
    [Fact]
    public async Task SyntheticProcessWorker()
    {
        var root = Environment.GetEnvironmentVariable("EMF_M5_SYNTHETIC_WORKER_ROOT");
        if (root is null) return;
        root = Path.GetFullPath(root);
        if (!root.StartsWith(Path.Combine(Path.GetTempPath(), "emf-m5-synthetic-"), StringComparison.Ordinal)
            || new DirectoryInfo(root).LinkTarget is not null
            || await File.ReadAllTextAsync(Path.Combine(root, "worker-allowed")) != "EMF-M5-SYNTHETIC-PROCESS-WORKER")
            throw new InvalidOperationException("Synthetic worker requires an explicitly marked temporary store.");
        var request = JsonSerializer.Deserialize<WorkerRequest>(await File.ReadAllTextAsync(Path.Combine(root, "worker-request.json")))!;
        var f = await ArtifactIngestionFixture.OpenWorkerAsync(root, request.ArtifactId, request.Operation);
        if (request.RecoverOnly)
        {
            var recovered = request.Resume ? await f.Service().ResumeAsync()
                : request.PauseReview
                    ? await f.Service(persistence: new FaultPersistence(f.Persistence, "RequiresReview", true) { OnFault = StopWorkerAsync }).RecoverOperationAsync(request.Operation.OperationId)
                    : await f.Restart().RecoverOperationAsync(request.Operation.OperationId);
            await File.WriteAllTextAsync(Path.Combine(root, "worker-recovered.json"), JsonSerializer.Serialize(new WorkerRecoveryResult(recovered.State, recovered.IsAdopted, f.Encryption.Encryptions,
                f.SecurityContext.Reviews.Any(x => x.Actor == f.SecurityContext.RecoveryActor && x.Operation == request.Operation.OperationId && x.Artifact == f.Id))));
            return;
        }
        async Task StopWorkerAsync()
        { await File.WriteAllTextAsync(Path.Combine(root, "worker-ready"), request.Checkpoint); await Task.Delay(Timeout.Infinite); }
        if (request.Checkpoint == "Encrypting") f.Encryption.EncryptHook = StopWorkerAsync;
        if (request.Checkpoint == "GenerationPrepared") f.Physical.Checkpoint = point => point == "CandidateDurable" ? StopWorkerAsync() : Task.CompletedTask;
        var staging = request.Checkpoint == "Staged" ? new CheckpointStaging(f.Staging, StopWorkerAsync) : null;
        var physical = new FaultStore(f.Physical)
        {
            AfterCreate = request.Checkpoint == "PhysicalCreated" ? StopWorkerAsync : null,
            AfterDelete = request.Checkpoint == "PhysicalDeleted" ? StopWorkerAsync : null
        };
        var persistence = new FaultPersistence(f.Persistence, request.Checkpoint, request.BeforeCommit)
        {
            OnFault = StopWorkerAsync
        };
        if (request.Cleanup) await f.Service(persistence: persistence, physical: physical).RecoverOperationAsync(request.Operation.OperationId);
        else await f.Service(persistence: persistence, physical: physical, staging: staging).IngestAsync(f.Draft, f.Content);
        throw new InvalidOperationException("Synthetic worker failed to reach requested checkpoint.");
    }
}
