using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using EMF.Core.Contracts.Malware;
using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts.Zip;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;
using EMF.Security.Ingestion;
using EMF.Tests.TestInfrastructure;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class ZipPhaseIIProcessRecoveryTests
{
    // Persisted worker setup contains evidence/configuration only, never a ZIP live grant.
    private sealed record Request(ArtifactId ControlArtifact, string OperationId, string Boundary,
        bool Recovery, DateTimeOffset Now, string PipeName);
    private sealed record Outcome(int Process, ZipOwnershipPurpose Purpose, ZipPhaseIIStop Stop, ZipParentSnapshot Parent);
    private sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private const string Marker = "EMF-ZIP-PHASE-II-SYNTHETIC-PROCESS-WORKER";
    private const string EnvironmentName = "EMF_ZIP_PHASE_II_SYNTHETIC_WORKER_ROOT";
    private static string RequestPath(string root) => Path.Combine(root, "zip-phase-ii-worker-request.json");
    private static string ResultPath(string root) => Path.Combine(root, "zip-phase-ii-worker-result.json");
    private static string MarkerPath(string root) => Path.Combine(root, "zip-phase-ii-worker-allowed");

    private static ProcessStartInfo Start(string root)
    {
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("vstest"); info.ArgumentList.Add(typeof(ZipPhaseIIProcessRecoveryTests).Assembly.Location);
        info.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=EMF.Tests.ZipPhaseIIProcessRecoveryTests.SyntheticPhaseIIProcessWorker");
        info.Environment[EnvironmentName] = root;
        foreach (var name in new[] { "EMF_ZIP_SYNTHETIC_WORKER_ROOT", "EMF_AZURE_OPENAI_LIVE", "EMF_AZURE_OPENAI_LIVE_TESTS", "EMF_AZURE_MONITOR_LIVE_TESTS" })
            info.Environment.Remove(name);
        return info;
    }

    private static async Task<int> CrashWorkerAsync(string root, string pipeName)
    {
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var process = Process.Start(Start(root)) ?? throw new InvalidOperationException("Unable to launch synthetic worker.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(55));
            var connection = pipe.WaitForConnectionAsync(timeout.Token);
            var exit = process.WaitForExitAsync(timeout.Token);
            if (await Task.WhenAny(connection, exit) == exit)
                throw new InvalidOperationException("Worker exited before crash boundary: " + await output + await error);
            await connection;
            using var reader = new StreamReader(pipe, leaveOpen: true);
            var runtimeProcess = int.Parse(await reader.ReadLineAsync(timeout.Token) ?? throw new InvalidDataException("Missing crash acknowledgement."));
            process.Kill(entireProcessTree: true); await exit;
            Assert.NotEqual(0, process.ExitCode);
            Assert.NotEqual(Environment.ProcessId, runtimeProcess);
            return runtimeProcess;
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            await output; await error;
        }
    }

    private static async Task<Outcome> RecoverWorkerAsync(string root)
    {
        using var process = Process.Start(Start(root)) ?? throw new InvalidOperationException("Unable to launch recovery worker.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(55));
            await process.WaitForExitAsync(timeout.Token);
            Assert.True(process.ExitCode == 0, await output + await error);
            return JsonSerializer.Deserialize<Outcome>(await File.ReadAllTextAsync(ResultPath(root)))!;
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            await output; await error;
        }
    }

    [Theory]
    [InlineData("A")]
    [InlineData("ChildPhysicalCreated")]
    [InlineData("B")]
    public async Task Actual_process_death_requires_fresh_recovery_root_and_preserves_parent_side_evidence(string boundary)
    {
        await using var fixture = await ZipOwnershipFixture.CreateAsync();
        var f = fixture.Runtime; var op = fixture.Admission.OperationId;
        var originalParentRetention = (await f.Journal.ReadParentRetentionAsync(op))!;
        await File.WriteAllTextAsync(MarkerPath(fixture.Evidence.Root), Marker);
        var pipeName = "emf-zip-phase-ii-" + Guid.NewGuid().ToString("N");
        var request = new Request(fixture.Evidence.Id, op, boundary, false, fixture.Clock.Now, pipeName);
        await File.WriteAllTextAsync(RequestPath(fixture.Evidence.Root), JsonSerializer.Serialize(request));
        var crashedProcess = await CrashWorkerAsync(fixture.Evidence.Root, pipeName);
        var interrupted = (await f.Journal.ReadAsync(op))!;
        Assert.Equal(1, interrupted.Fence.Epoch); Assert.NotNull(interrupted.Plan);
        var plan = interrupted.Plan!; var budget = interrupted.Budget;
        var childBefore = await f.Journal.ReadRetentionAsync(op, 0);
        var proofBefore = await IntegrityAsync(fixture.Evidence.DatabasePath, op);
        var receiptBefore = childBefore is null ? null : await f.ChildStorage.GetReceiptAsync(new(childBefore.Identity.CreateOperationId));
        var parentReads = f.Count("parent-decrypt"); var childEncrypts = f.Count("child-encrypt");
        var extractionStarts = f.Count("extraction-start");
        if (boundary == "A") { Assert.Null(childBefore); Assert.Null(proofBefore); Assert.Equal(0, budget.ExtractionAttempts); }
        else
        {
            Assert.NotNull(childBefore); Assert.NotNull(proofBefore); Assert.NotNull(receiptBefore);
            if (boundary == "ChildPhysicalCreated")
            {
                Assert.Equal(ZipRetentionState.CandidateBound, childBefore!.State);
                Assert.Null(childBefore.CreateReceipt);
            }
            else { Assert.Equal(ZipRetentionState.Created, childBefore!.State); Assert.Equal(ZipEntryState.Materialized, interrupted.Entries[0].State); }
        }
        File.Delete(fixture.Evidence.SourcePath); // Recovery must use private retained evidence.
        var recoveryRequest = request with { Recovery = true, Now = request.Now.AddMinutes(20), PipeName = "unused" };
        await File.WriteAllTextAsync(RequestPath(fixture.Evidence.Root), JsonSerializer.Serialize(recoveryRequest));
        var recovered = await RecoverWorkerAsync(fixture.Evidence.Root);
        Assert.NotEqual(crashedProcess, recovered.Process); Assert.NotEqual(Environment.ProcessId, recovered.Process);
        Assert.Equal(ZipOwnershipPurpose.Recovery, recovered.Purpose); Assert.Equal(ZipPhaseIIStop.AwaitingScanner, recovered.Stop);
        var final = (await f.Journal.ReadAsync(op))!;
        Assert.Equal(2, final.Fence.Epoch); Assert.Equal(-1, final.Fence.ConfirmedOrdinal);
        Assert.Equal(ZipParentState.Processing, final.State);
        Assert.Equal(ZipEntryState.Materialized, final.Entries[0].State);
        Assert.Equal(plan.Hash, final.Plan!.Hash);
        Assert.Equal(plan.Entries.Select(e => (e.ChildOperationId, e.ProvisionalArtifactId)),
            final.Plan.Entries.Select(e => (e.ChildOperationId, e.ProvisionalArtifactId)));
        Assert.Equal(budget.PreflightAttempts, final.Budget.PreflightAttempts);
        Assert.Equal(budget.PreflightBytes, final.Budget.PreflightBytes);
        Assert.Equal(1, final.Budget.ExtractionAttempts); Assert.Equal(0, final.Budget.ScannerAttempts);
        Assert.Equal(0, final.Budget.IngestionAttempts); Assert.Equal(0, final.Budget.RejectedEntries);
        Assert.True(final.Budget.ParentReadBytes >= budget.ParentReadBytes);
        Assert.True(final.Budget.ExpandedReserved >= budget.ExpandedReserved);
        Assert.True(final.Budget.ExpandedProduced >= budget.ExpandedProduced);
        Assert.True(final.Budget.CrcBytes >= budget.CrcBytes);
        Assert.Equal(1, f.Count("parent-encrypt")); Assert.Equal(1, f.Count("child-encrypt"));
        Assert.Equal(0, f.Count("scan")); Assert.Equal(0, f.Count("ingestion-encrypt"));
        var parentAfter = (await f.Journal.ReadParentRetentionAsync(op))!;
        Assert.Equal(originalParentRetention, parentAfter);
        var childAfter = (await f.Journal.ReadRetentionAsync(op, 0))!;
        Assert.Equal(ZipRetentionState.Created, childAfter.State);
        Assert.Null(childAfter.ReleaseReceipt); Assert.Null(childAfter.CandidateCleanup);
        if (boundary != "A")
        {
            Assert.Equal(childBefore!.Identity, childAfter.Identity); Assert.Equal(childBefore.CandidateHash, childAfter.CandidateHash);
            Assert.Equal(receiptBefore, childAfter.CreateReceipt); Assert.Equal(proofBefore, await IntegrityAsync(fixture.Evidence.DatabasePath, op));
            Assert.Equal(budget, final.Budget);
            Assert.Equal(parentReads, f.Count("parent-decrypt")); Assert.Equal(childEncrypts, f.Count("child-encrypt"));
            Assert.Equal(extractionStarts, f.Count("extraction-start"));
        }
        else
        {
            Assert.Equal(parentReads + 1, f.Count("parent-decrypt"));
            Assert.Equal(extractionStarts + 1, f.Count("extraction-start"));
        }
        foreach (var table in new[] { "ZipExtractionScans", "ArtifactIngestionOperations", "ArtifactIngestionIntents", "ArtifactIngestionReviews", "ZipExtractionAcknowledgements", "Relationships", "Artifacts" })
            Assert.Equal(0, await CountAsync(fixture.Evidence.DatabasePath, table));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Driver.RunAsync(interrupted));
        var requestJson = await File.ReadAllTextAsync(RequestPath(fixture.Evidence.Root));
        Assert.DoesNotContain("ValidUntilUtc", requestJson); Assert.DoesNotContain("Revoked", requestJson);
    }

    private static async Task<string?> IntegrityAsync(string database, string operation)
    {
        await using var c = new SqliteConnection("Data Source=" + database); await c.OpenAsync();
        using var q = c.CreateCommand(); q.CommandText = "SELECT VerificationJson FROM ZipExtractionVerifications WHERE ParentOperationId=$op AND FileOrdinal=0";
        q.Parameters.AddWithValue("$op", operation); return await q.ExecuteScalarAsync() as string;
    }
    private static async Task<long> CountAsync(string database, string table)
    {
        await using var c = new SqliteConnection("Data Source=" + database); await c.OpenAsync();
        using var q = c.CreateCommand(); q.CommandText = "SELECT COUNT(*) FROM " + table;
        return Convert.ToInt64(await q.ExecuteScalarAsync());
    }
    private static ZipDurableProfile ReadProfile(ZipParentAdmission admission)
    {
        using var document = JsonDocument.Parse(admission.Binding.ProfileJson); var p = document.RootElement;
        string Text(string name) => p.GetProperty(name).GetString()!;
        TimeSpan Duration(string name) => TimeSpan.FromTicks(p.GetProperty(name).GetInt64());
        var profile = new ZipDurableProfile(Text("parentNamespace"), Text("childNamespace"),
            new MalwareScannerPolicy(Text("scannerProvider"), Text("scannerVersion"), Text("scannerPolicyHash")), Text("protectionContract"),
            Duration("scannerTimeoutTicks"), Duration("ownershipLeaseTicks"), Duration("renewalIntervalTicks"));
        profile.ValidateRecoveryBinding(admission.Binding.ProfileJson, admission.Binding.ProfileHash); return profile;
    }

    [Fact]
    public async Task SyntheticPhaseIIProcessWorker()
    {
        var root = Environment.GetEnvironmentVariable(EnvironmentName); if (root is null) return;
        root = Path.GetFullPath(root);
        if (!root.StartsWith(Path.Combine(Path.GetTempPath(), "emf-m5-synthetic-"), StringComparison.Ordinal) ||
            new DirectoryInfo(root).LinkTarget is not null || await File.ReadAllTextAsync(MarkerPath(root)) != Marker)
            throw new InvalidOperationException("Phase II worker requires explicitly marked synthetic storage.");
        var request = JsonSerializer.Deserialize<Request>(await File.ReadAllTextAsync(RequestPath(root)))!;
        // Generic fixture construction only; no ZIP grant is read from disk or inherited.
        var genericFixtureOperation = new AuthenticatedIngestionOperation(ArtifactContentOperationId.New(),
            new(Guid.NewGuid().ToString("N")), "synthetic-phase-ii-unused-generic-host");
        var evidence = await ArtifactIngestionFixture.OpenWorkerAsync(root, request.ControlArtifact, genericFixtureOperation);
        var clock = new Clock(request.Now); var f = new ZipDurableRuntimeFixture(evidence, clock);
        var admission = (await f.Journal.ReadAdmissionAsync(request.OperationId))!; var profile = ReadProfile(admission);
        var parent = (await f.Journal.ReadAsync(request.OperationId))!;
        var purpose = request.Recovery ? ZipOwnershipPurpose.Recovery : ZipOwnershipPurpose.InitialExecution;
        var ownership = new ZipAuthorizedOwnershipService(f.Journal, f.Journal, profile, clock);
        if (request.Recovery)
        {
            // A new process cannot reinterpret a committed first attempt as initial work.
            // Nor can durable eligibility substitute for current, unrevoked recovery authority.
            var originalProjection = JsonSerializer.Serialize(parent);
            var currentProjection = new ZipOwnershipClaimRequest(ZipOwnershipPurpose.InitialExecution, admission,
                parent.Fence, parent.OwnerUntil, "phase-ii-restart", profile.OwnershipLease);
            await using (var forbiddenInitial = new SyntheticZipExecutionGrant(admission, ZipOwnershipPurpose.InitialExecution, clock))
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ZipExecutionAttempt.ClaimAsync(
                    ownership, forbiddenInitial, currentProjection, clock));
            Assert.Equal(originalProjection, JsonSerializer.Serialize((await f.Journal.ReadAsync(request.OperationId))!));
            await using (var revokedRecovery = new SyntheticZipExecutionGrant(admission, ZipOwnershipPurpose.Recovery, clock))
            {
                revokedRecovery.Revoke();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ZipExecutionAttempt.ClaimAsync(
                    ownership, revokedRecovery, currentProjection with { Purpose = ZipOwnershipPurpose.Recovery }, clock));
            }
            Assert.Equal(originalProjection, JsonSerializer.Serialize((await f.Journal.ReadAsync(request.OperationId))!));
            Assert.Equal(admission, await f.Journal.ReadAdmissionAsync(request.OperationId));
        }
        await using var grant = new SyntheticZipExecutionGrant(admission, purpose, clock);
        var claim = new ZipOwnershipClaimRequest(purpose, admission, parent.Fence, parent.OwnerUntil,
            request.Recovery ? "phase-ii-restart" : "phase-ii-first", profile.OwnershipLease);
        await using var attempt = await ZipExecutionAttempt.ClaimAsync(ownership, grant, claim, clock);
        async Task Pause()
        {
            await using var pipe = new NamedPipeClientStream(".", request.PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(55)); await pipe.ConnectAsync(timeout.Token);
            await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync(Environment.ProcessId.ToString());
            await new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task;
        }
        if (!request.Recovery)
        {
            f.Driver.Checkpoint = name => name == request.Boundary ? Pause() : Task.CompletedTask;
            if (request.Boundary == "ChildPhysicalCreated")
                f.ChildRetention.Checkpoint = name => name == "PhysicalCreated" ? Pause() : Task.CompletedTask;
        }
        var result = await f.Driver.RunAsync(attempt);
        await File.WriteAllTextAsync(ResultPath(root), JsonSerializer.Serialize(new Outcome(Environment.ProcessId, grant.Purpose, result.Stop, result.Parent)));
    }
}
