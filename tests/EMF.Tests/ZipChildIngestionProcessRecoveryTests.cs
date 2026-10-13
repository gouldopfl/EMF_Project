using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using EMF.Core.Contracts.Ingestion;
using EMF.Core.Contracts.Malware;
using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts.Zip;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;
using EMF.Security.Ingestion;
using EMF.Tests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace EMF.Tests;

[Collection(ZipProcessWideGateCollection.Name)]
public sealed class ZipChildIngestionProcessRecoveryTests
{
    private sealed record Request(ArtifactId ControlArtifact, string OperationId,
        string AuthorizedOperationId, DateTimeOffset Now, bool Recovery, string PipeName);
    private sealed record Outcome(int Process, ZipOwnershipPurpose Purpose, ZipChildIngestionStop Stop,
        ZipParentSnapshot Parent, int AuditReads);
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => now; }

    private const string Marker = "EMF-ZIP-CHILD-INGESTION-SYNTHETIC-PROCESS-WORKER";
    private const string EnvironmentName = "EMF_ZIP_CHILD_INGESTION_SYNTHETIC_WORKER_ROOT";
    private static string RequestPath(string root) => Path.Combine(root, "zip-child-ingestion-worker-request.json");
    private static string ResultPath(string root) => Path.Combine(root, "zip-child-ingestion-worker-result.json");
    private static string MarkerPath(string root) => Path.Combine(root, "zip-child-ingestion-worker-allowed");

    private static ProcessStartInfo Start(string root)
    {
        var info = new ProcessStartInfo("dotnet")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("vstest");
        info.ArgumentList.Add(typeof(ZipChildIngestionProcessRecoveryTests).Assembly.Location);
        info.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=EMF.Tests.ZipChildIngestionProcessRecoveryTests.SyntheticChildIngestionProcessWorker");
        info.Environment[EnvironmentName] = root;
        foreach (var name in new[] { "EMF_ZIP_SYNTHETIC_WORKER_ROOT", "EMF_ZIP_PHASE_II_SYNTHETIC_WORKER_ROOT",
            "EMF_ZIP_SCANNER_SYNTHETIC_WORKER_ROOT", "EMF_AZURE_OPENAI_LIVE", "EMF_AZURE_OPENAI_LIVE_TESTS",
            "EMF_AZURE_MONITOR_LIVE_TESTS" })
            info.Environment.Remove(name);
        return info;
    }

    private static async Task<int> CrashWorkerAsync(string root, string pipeName)
    {
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var process = Process.Start(Start(root)) ?? throw new InvalidOperationException("Unable to launch child worker.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(55));
            var connection = pipe.WaitForConnectionAsync(timeout.Token);
            var exit = process.WaitForExitAsync(timeout.Token);
            if (await Task.WhenAny(connection, exit) == exit)
                throw new InvalidOperationException("Child worker exited before proof boundary: " + await output + await error);
            await connection;
            using var reader = new StreamReader(pipe, leaveOpen: true);
            var pid = int.Parse(await reader.ReadLineAsync(timeout.Token)
                ?? throw new InvalidDataException("Missing child crash acknowledgement."));
            process.Kill(entireProcessTree: true);
            await exit;
            Assert.NotEqual(0, process.ExitCode);
            Assert.NotEqual(Environment.ProcessId, pid);
            return pid;
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
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
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

    private static async Task<long> CountAsync(string path, string table)
    {
        await using var c = new SqliteConnection("Data Source=" + path);
        await c.OpenAsync();
        using var q = c.CreateCommand();
        q.CommandText = table switch
        {
            "Artifacts" or "Relationships" or "ZipExtractionAcknowledgements" or
                "ArtifactIngestionCandidateCreations" or "ArtifactIngestionAdoptions" => "SELECT COUNT(*) FROM " + table,
            "Delivered" => "SELECT COUNT(*) FROM ArtifactIngestionAudit WHERE Delivered=1",
            _ => throw new ArgumentOutOfRangeException(nameof(table))
        };
        return Convert.ToInt64(await q.ExecuteScalarAsync());
    }

    private static async Task<long> ReservationCountAsync(string path, string operationId, int ordinal)
    {
        await using var c = new SqliteConnection("Data Source=" + path);
        await c.OpenAsync();
        using var q = c.CreateCommand();
        q.CommandText = "SELECT COUNT(*) FROM ZipExtractionReservations WHERE ParentOperationId=$op AND FileOrdinal=$file AND Kind=$kind";
        q.Parameters.AddWithValue("$op", operationId);
        q.Parameters.AddWithValue("$file", ordinal);
        q.Parameters.AddWithValue("$kind", (int)ZipWorkKind.Ingestion);
        return Convert.ToInt64(await q.ExecuteScalarAsync());
    }

    private static ZipDurableProfile ReadProfile(ZipParentAdmission admission)
    {
        using var document = JsonDocument.Parse(admission.Binding.ProfileJson);
        var p = document.RootElement;
        string Text(string name) => p.GetProperty(name).GetString()!;
        TimeSpan Duration(string name) => TimeSpan.FromTicks(p.GetProperty(name).GetInt64());
        var profile = new ZipDurableProfile(Text("parentNamespace"), Text("childNamespace"),
            new MalwareScannerPolicy(Text("scannerProvider"), Text("scannerVersion"), Text("scannerPolicyHash")),
            Text("protectionContract"), Duration("scannerTimeoutTicks"),
            Duration("ownershipLeaseTicks"), Duration("renewalIntervalTicks"));
        profile.ValidateRecoveryBinding(admission.Binding.ProfileJson, admission.Binding.ProfileHash);
        return profile;
    }

    [Fact]
    public async Task Real_death_after_generic_completion_before_zip_proof_requires_fresh_recovery_without_republication()
    {
        await using var setup = await ZipScannerAuthorityTests.Setup.Create();
        var fixture = setup.Fixture;
        Assert.Equal(ZipScannerStop.AwaitingChildIngestion, (await setup.Scan()).Stop);
        await setup.Attempt.DisposeAsync();
        await setup.Root.DisposeAsync();
        var root = fixture.Evidence.Root;
        var scanned = (await fixture.Journal.ReadAsync(fixture.Admission.OperationId))!;
        Assert.Equal(ZipEntryState.Scanned, scanned.Entries.Single().State);
        Assert.Equal(0, scanned.Budget.IngestionAttempts);
        Assert.Equal(0, await ReservationCountAsync(fixture.Evidence.DatabasePath, fixture.Admission.OperationId, 0));
        Assert.Equal(0, await CountAsync(fixture.Evidence.DatabasePath, "Artifacts"));
        Assert.Equal(0, await CountAsync(fixture.Evidence.DatabasePath, "ArtifactIngestionCandidateCreations"));
        Assert.Equal(0, await CountAsync(fixture.Evidence.DatabasePath, "Delivered"));
        Assert.Equal(0, await CountAsync(fixture.Evidence.DatabasePath, "ZipExtractionAcknowledgements"));
        Assert.Equal(0, await CountAsync(fixture.Evidence.DatabasePath, "Relationships"));

        await using (var forbidden = new SyntheticZipExecutionGrant(fixture.Admission,
            ZipOwnershipPurpose.InitialExecution, fixture.Clock))
        {
            var claim = await fixture.RequestAsync(forbidden.Purpose);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ZipExecutionAttempt.ClaimAsync(
                new(fixture.Journal, fixture.Journal, fixture.Profile, fixture.Clock), forbidden, claim, fixture.Clock));
        }
        Assert.Equal(JsonSerializer.Serialize(scanned),
            JsonSerializer.Serialize((await fixture.Journal.ReadAsync(fixture.Admission.OperationId))!));
        Assert.Equal(0, await ReservationCountAsync(fixture.Evidence.DatabasePath, fixture.Admission.OperationId, 0));
        Assert.Equal(0, await CountAsync(fixture.Evidence.DatabasePath, "Artifacts"));

        await File.WriteAllTextAsync(MarkerPath(root), Marker);
        var pipeName = "emf-zip-child-" + Guid.NewGuid().ToString("N");
        var request = new Request(fixture.Evidence.Id, fixture.Admission.OperationId,
            fixture.Admission.Binding.AuthorizedOperationId, fixture.Clock.Now.AddMinutes(16), false, pipeName);
        await File.WriteAllTextAsync(RequestPath(root), JsonSerializer.Serialize(request));
        var crashed = await CrashWorkerAsync(root, pipeName);

        var interrupted = (await fixture.Journal.ReadAsync(request.OperationId))!;
        var entry = interrupted.Entries.Single();
        Assert.Equal(ZipEntryState.Scanned, entry.State);
        Assert.Null(entry.Ingestion);
        Assert.Equal(1, interrupted.Budget.IngestionAttempts);
        Assert.Equal(1, await ReservationCountAsync(fixture.Evidence.DatabasePath, request.OperationId, 0));
        Assert.True(await fixture.Journal.HasIngestionReservationAsync(interrupted.Fence, 0));
        await Assert.ThrowsAsync<ZipFenceException>(() =>
            fixture.Journal.HasIngestionReservationAsync(scanned.Fence, 0));
        await Assert.ThrowsAsync<ZipFenceException>(() =>
            fixture.Journal.HasIngestionReservationAsync(interrupted.Fence, 1));
        await Assert.ThrowsAsync<ZipFenceException>(() =>
            fixture.Journal.HasIngestionReservationAsync(
                interrupted.Fence with { OperationId = "unrelated-parent" }, 0));
        Assert.Equal(-1, interrupted.Fence.ConfirmedOrdinal);
        var child = new ArtifactContentOperationId(entry.Plan.ChildOperationId!);
        await using (var session = await fixture.Evidence.Persistence.AcquireAsync(child))
        {
            Assert.Equal(ArtifactIngestionState.Completed, session.Intent?.State);
            Assert.True(await session.HasAdoptionEvidenceAsync(new(entry.Plan.ProvisionalArtifactId!)));
            Assert.NotNull(await session.ReadResultAsync());
        }
        var receipt = await fixture.Evidence.Physical.GetMutationOutcomeAsync(child);
        Assert.NotNull(receipt);
        var obligations = await fixture.Evidence.Persistence.ReadAuditObligationsAsync(child);
        Assert.NotEmpty(obligations);
        foreach (var obligation in obligations)
            Assert.NotNull(await fixture.Evidence.Audit.FindVerifiedAsync(new(obligation.EventId.Value)));
        var delivered = await CountAsync(fixture.Evidence.DatabasePath, "Delivered");
        Assert.Equal(obligations.Count, delivered);
        Assert.Equal(1, await CountAsync(fixture.Evidence.DatabasePath, "Artifacts"));
        Assert.Equal(1, await CountAsync(fixture.Evidence.DatabasePath, "ArtifactIngestionCandidateCreations"));
        Assert.Equal(1, await CountAsync(fixture.Evidence.DatabasePath, "ArtifactIngestionAdoptions"));

        await File.WriteAllTextAsync(RequestPath(root), JsonSerializer.Serialize(request with
        { Recovery = true, Now = fixture.Clock.Now.AddMinutes(16), PipeName = "unused" }));
        var recovered = await RecoverWorkerAsync(root);
        Assert.NotEqual(crashed, recovered.Process);
        Assert.Equal(ZipOwnershipPurpose.Recovery, recovered.Purpose);
        Assert.Equal(ZipChildIngestionStop.Ingested, recovered.Stop);
        Assert.True(recovered.AuditReads > 0);
        var final = (await fixture.Journal.ReadAsync(request.OperationId))!;
        Assert.Equal(ZipEntryState.Ingested, final.Entries.Single().State);
        Assert.NotNull(final.Entries.Single().Ingestion);
        Assert.Equal(1, final.Budget.IngestionAttempts);
        Assert.Equal(1, await ReservationCountAsync(fixture.Evidence.DatabasePath, request.OperationId, 0));
        Assert.Equal(-1, final.Fence.ConfirmedOrdinal);
        Assert.Equal(ZipParentState.Processing, final.State);
        Assert.Equal(receipt, await fixture.Evidence.Physical.GetMutationOutcomeAsync(child));
        Assert.Equal(1, await CountAsync(fixture.Evidence.DatabasePath, "Artifacts"));
        Assert.Equal(1, await CountAsync(fixture.Evidence.DatabasePath, "ArtifactIngestionCandidateCreations"));
        Assert.Equal(1, await CountAsync(fixture.Evidence.DatabasePath, "ArtifactIngestionAdoptions"));
        Assert.Equal(delivered, await CountAsync(fixture.Evidence.DatabasePath, "Delivered"));
        Assert.Equal(0, await CountAsync(fixture.Evidence.DatabasePath, "Relationships"));
        Assert.Equal(0, await CountAsync(fixture.Evidence.DatabasePath, "ZipExtractionAcknowledgements"));
        var retention = await fixture.Journal.ReadRetentionAsync(request.OperationId, 0);
        Assert.Equal(ZipRetentionState.Created, retention?.State);
        Assert.Null(retention?.ReleaseReceipt);
    }

    [Fact]
    public async Task SyntheticChildIngestionProcessWorker()
    {
        var root = Environment.GetEnvironmentVariable(EnvironmentName);
        if (root is null) return;
        root = Path.GetFullPath(root);
        if (!root.StartsWith(Path.Combine(Path.GetTempPath(), "emf-m5-synthetic-"), StringComparison.Ordinal) ||
            new DirectoryInfo(root).LinkTarget is not null || await File.ReadAllTextAsync(MarkerPath(root)) != Marker)
            throw new InvalidOperationException("Child worker requires explicitly marked synthetic storage.");
        var request = JsonSerializer.Deserialize<Request>(await File.ReadAllTextAsync(RequestPath(root)))!;
        var operation = new AuthenticatedIngestionOperation(new("synthetic-worker-unused"),
            new(request.AuthorizedOperationId), "synthetic-zip-child");
        var evidence = await ArtifactIngestionFixture.OpenWorkerAsync(root, request.ControlArtifact, operation);
        var clock = new Clock(request.Now);
        var runtime = new ZipDurableRuntimeFixture(evidence, clock);
        var admission = (await runtime.Journal.ReadAdmissionAsync(request.OperationId))!;
        runtime.ChildParentOperationId = admission.OperationId;
        var profile = ReadProfile(admission);
        var ownership = new ZipAuthorizedOwnershipService(runtime.Journal, runtime.Journal, profile, clock);
        var parent = (await runtime.Journal.ReadAsync(request.OperationId))!;
        if (request.Recovery)
        {
            var forbiddenRequest = new ZipOwnershipClaimRequest(ZipOwnershipPurpose.InitialExecution,
                admission, parent.Fence, parent.OwnerUntil, "worker", profile.OwnershipLease);
            await using var forbidden = new SyntheticZipExecutionGrant(admission, ZipOwnershipPurpose.InitialExecution, clock);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ZipExecutionAttempt.ClaimAsync(
                ownership, forbidden, forbiddenRequest, clock));
            Assert.Equal(JsonSerializer.Serialize(parent),
                JsonSerializer.Serialize((await runtime.Journal.ReadAsync(request.OperationId))!));
        }
        var purpose = ZipOwnershipPurpose.Recovery;
        await using var grant = new SyntheticZipExecutionGrant(admission, purpose, clock);
        grant.GuardedChildOpener = (p, e, gate, ct) => runtime.Host.OpenGuardedAsync(p, e, gate, ct);
        var auditReads = 0;
        grant.OnEffectRequest = effect =>
        {
            if (effect is ZipChildIngestionEffectRequest { Effect: ZipChildIngestionEffect.AuditRead })
                auditReads++;
        };
        var claim = new ZipOwnershipClaimRequest(purpose, admission, parent.Fence, parent.OwnerUntil,
            "worker", profile.OwnershipLease);
        await using var attempt = await ZipExecutionAttempt.ClaimAsync(ownership, grant, claim, clock);
        if (!request.Recovery)
        {
            Assert.Equal(ZipEntryState.Scanned,
                (await runtime.Journal.ReadAsync(request.OperationId))!.Entries.Single().State);
            runtime.Ingest.Checkpoint = point => point == "CoordinatorReturned" ? PauseAsync() : Task.CompletedTask;
        }
        var result = await runtime.Driver.RunChildIngestionAsync(attempt);
        await File.WriteAllTextAsync(ResultPath(root),
            JsonSerializer.Serialize(new Outcome(Environment.ProcessId, purpose, result.Stop, result.Parent, auditReads)));

        async Task PauseAsync()
        {
            await using var pipe = new NamedPipeClientStream(".", request.PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(55));
            await pipe.ConnectAsync(timeout.Token);
            await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync(Environment.ProcessId.ToString());
            await new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task;
        }
    }
}
