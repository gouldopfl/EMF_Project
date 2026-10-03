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
        string Checkpoint, bool BeforeCommit, bool Cleanup, bool RecoverOnly = false);
    private sealed record WorkerRecoveryResult(ArtifactIngestionState State, bool IsAdopted);
    [Theory]
    [InlineData("Prepared", false, false)]
    [InlineData("Candidate", false, false)]
    [InlineData("ContentCreated", false, false)]
    [InlineData("PhysicalCreated", false, false)]
    [InlineData("MetadataCommitted", true, false)]
    [InlineData("MetadataCommitted", false, false)]
    [InlineData("Completed", false, false)]
    [InlineData("CleanupClaimed", false, true)]
    [InlineData("Cleaned", false, true)]
    [InlineData("PhysicalDeleted", false, true)]
    public async Task Killed_worker_releases_metadata_fence_and_recovery_honors_committed_state(string checkpoint, bool beforeCommit, bool cleanup)
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
            File.Delete(f.SourcePath);
            await File.WriteAllTextAsync(Path.Combine(f.Root, "worker-request.json"), JsonSerializer.Serialize(new WorkerRequest(f.Id, f.SecurityContext.Operation, checkpoint, beforeCommit, cleanup, true)));
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
            var adopted = !beforeCommit && checkpoint is "MetadataCommitted" or "Completed";
            Assert.Equal(adopted, recovered.IsAdopted); Assert.Equal(adopted, repeated.IsAdopted);
            Assert.Equal(adopted ? ArtifactIngestionState.Completed : ArtifactIngestionState.Cleaned, repeated.State);
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
            var recovered = await f.Restart().RecoverOperationAsync(request.Operation.OperationId);
            await File.WriteAllTextAsync(Path.Combine(root, "worker-recovered.json"), JsonSerializer.Serialize(new WorkerRecoveryResult(recovered.State, recovered.IsAdopted)));
            return;
        }
        async Task StopWorkerAsync()
        { await File.WriteAllTextAsync(Path.Combine(root, "worker-ready"), request.Checkpoint); await Task.Delay(Timeout.Infinite); }
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
        else await f.Service(persistence: persistence, physical: physical).IngestAsync(f.Draft, f.Content);
        throw new InvalidOperationException("Synthetic worker failed to reach requested checkpoint.");
    }
}
