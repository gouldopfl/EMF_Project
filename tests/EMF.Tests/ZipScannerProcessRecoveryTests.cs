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

[Collection(ZipProcessWideGateCollection.Name)]
public sealed class ZipScannerProcessRecoveryTests
{
    // Persisted worker setup contains evidence/configuration only, never a ZIP live grant.
    private sealed record Request(ArtifactId ControlArtifact, string OperationId, string Boundary,
        bool Recovery, DateTimeOffset Now, string PipeName);
    private sealed record Outcome(int Process, ZipOwnershipPurpose Purpose, ZipScannerStop Stop, ZipParentSnapshot Parent);
    private sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private const string Marker = "EMF-ZIP-SCANNER-SYNTHETIC-PROCESS-WORKER";
    private const string EnvironmentName = "EMF_ZIP_SCANNER_SYNTHETIC_WORKER_ROOT";
    private static string RequestPath(string root) => Path.Combine(root, "zip-scanner-worker-request.json");
    private static string ResultPath(string root) => Path.Combine(root, "zip-scanner-worker-result.json");
    private static string MarkerPath(string root) => Path.Combine(root, "zip-scanner-worker-allowed");

    private static ProcessStartInfo Start(string root)
    {
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("vstest"); info.ArgumentList.Add(typeof(ZipScannerProcessRecoveryTests).Assembly.Location);
        info.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=EMF.Tests.ZipScannerProcessRecoveryTests.SyntheticScannerProcessWorker");
        info.Environment[EnvironmentName] = root;
        foreach (var name in new[] { "EMF_ZIP_SYNTHETIC_WORKER_ROOT", "EMF_ZIP_PHASE_II_SYNTHETIC_WORKER_ROOT", "EMF_AZURE_OPENAI_LIVE", "EMF_AZURE_OPENAI_LIVE_TESTS", "EMF_AZURE_MONITOR_LIVE_TESTS" })
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
    [InlineData("Reserved")]
    [InlineData("ScannerReturned")]
    [InlineData("ObservationRecorded")]
    [InlineData("Progressed")]
    public async Task Real_process_death_requires_fresh_current_recovery_and_preserves_scan_facts_and_charged_budgets(string boundary)
    {
        await using var setup=await ZipScannerAuthorityTests.Setup.Create();var fixture=setup.Fixture;var f=fixture.Runtime;var op=fixture.Admission.OperationId;
        var initial=(await f.Journal.ReadAsync(op))!;var parentRetention=(await f.Journal.ReadParentRetentionAsync(op))!;
        var childRetention=(await f.Journal.ReadRetentionAsync(op,0))!;var reads=f.Count("parent-decrypt");
        await File.WriteAllTextAsync(MarkerPath(fixture.Evidence.Root),Marker);
        var pipeName="emf-zip-scanner-"+Guid.NewGuid().ToString("N");
        var request=new Request(fixture.Evidence.Id,op,boundary,false,fixture.Clock.Now.AddMinutes(16),pipeName);
        await File.WriteAllTextAsync(RequestPath(fixture.Evidence.Root),JsonSerializer.Serialize(request));
        var crashed=await CrashWorkerAsync(fixture.Evidence.Root,pipeName);
        var interrupted=(await f.Journal.ReadAsync(op))!;Assert.Equal(2,interrupted.Fence.Epoch);
        Assert.Equal(1,interrupted.Budget.ScannerAttempts);Assert.Equal(-1,interrupted.Fence.ConfirmedOrdinal);
        if(boundary is "ObservationRecorded" or "Progressed")Assert.NotNull(interrupted.Entries[0].EvidenceJson);
        else Assert.Null(interrupted.Entries[0].EvidenceJson);
        Assert.Equal(boundary=="Progressed"?ZipEntryState.Scanned:ZipEntryState.Materialized,interrupted.Entries[0].State);
        var scanCount=f.Count("scan");var decrypts=f.Count("child-decrypt");
        var frozen=interrupted.Entries[0].EvidenceJson;
        await File.WriteAllTextAsync(RequestPath(fixture.Evidence.Root),JsonSerializer.Serialize(request with{Recovery=true,Now=fixture.Clock.Now.AddMinutes(32),PipeName="unused"}));
        var recovered=await RecoverWorkerAsync(fixture.Evidence.Root);
        Assert.NotEqual(crashed,recovered.Process);Assert.Equal(ZipOwnershipPurpose.Recovery,recovered.Purpose);
        Assert.Equal(ZipScannerStop.AwaitingChildIngestion,recovered.Stop);var final=(await f.Journal.ReadAsync(op))!;
        Assert.Equal(3,final.Fence.Epoch);Assert.Equal(ZipEntryState.Scanned,final.Entries[0].State);
        Assert.Equal(initial.Plan!.Hash,final.Plan!.Hash);Assert.Equal(initial.Entries[0].Plan,final.Entries[0].Plan);
        Assert.Equal(parentRetention,await f.Journal.ReadParentRetentionAsync(op));Assert.Equal(childRetention,await f.Journal.ReadRetentionAsync(op,0));
        Assert.Equal(reads,f.Count("parent-decrypt"));
        if(frozen is not null)
        {Assert.Equal(frozen,final.Entries[0].EvidenceJson);Assert.Equal(interrupted.Budget,final.Budget);Assert.Equal(scanCount,f.Count("scan"));Assert.Equal(decrypts,f.Count("child-decrypt"));}
        else
        {Assert.Equal(2,final.Budget.ScannerAttempts);Assert.Equal(2*final.Entries[0].Retained!.Length,final.Budget.ScannerReserved);Assert.Equal(final.Entries[0].Retained!.Length,final.Budget.ReplayScanner);}
        ZipScannerAuthorityTests.NoDownstream(fixture,final);
        foreach(var table in new[]{"ArtifactIngestionOperations","ArtifactIngestionIntents","ArtifactIngestionReviews","ZipExtractionAcknowledgements","Relationships","Artifacts"})
            Assert.Equal(0,await CountAsync(fixture.Evidence.DatabasePath,table));
        var disk=await File.ReadAllTextAsync(RequestPath(fixture.Evidence.Root));Assert.DoesNotContain("ValidUntilUtc",disk);Assert.DoesNotContain("Revoked",disk);Assert.DoesNotContain("Recorder",disk);
    }
    private static async Task<long> CountAsync(string database,string table)
    {
        await using var c=new SqliteConnection("Data Source="+database);await c.OpenAsync();using var q=c.CreateCommand();q.CommandText="SELECT COUNT(*) FROM "+table;
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
    public async Task SyntheticScannerProcessWorker()
    {
        var root = Environment.GetEnvironmentVariable(EnvironmentName); if (root is null) return;
        root = Path.GetFullPath(root);
        if (!root.StartsWith(Path.Combine(Path.GetTempPath(), "emf-m5-synthetic-"), StringComparison.Ordinal) ||
            new DirectoryInfo(root).LinkTarget is not null || await File.ReadAllTextAsync(MarkerPath(root)) != Marker)
            throw new InvalidOperationException("Scanner worker requires explicitly marked synthetic storage.");
        var request = JsonSerializer.Deserialize<Request>(await File.ReadAllTextAsync(RequestPath(root)))!;
        // Generic fixture construction only; no ZIP grant is read from disk or inherited.
        var genericFixtureOperation = new AuthenticatedIngestionOperation(ArtifactContentOperationId.New(),
            new(Guid.NewGuid().ToString("N")), "synthetic-scanner-unused-generic-host");
        var evidence = await ArtifactIngestionFixture.OpenWorkerAsync(root, request.ControlArtifact, genericFixtureOperation);
        var clock = new Clock(request.Now); var f = new ZipDurableRuntimeFixture(evidence, clock);
        var admission = (await f.Journal.ReadAdmissionAsync(request.OperationId))!; var profile = ReadProfile(admission);
        var parent = (await f.Journal.ReadAsync(request.OperationId))!;
        var purpose = ZipOwnershipPurpose.Recovery;
        var ownership = new ZipAuthorizedOwnershipService(f.Journal, f.Journal, profile, clock);
        {
            // A new process cannot reinterpret a committed first attempt as initial work.
            // Nor can durable eligibility substitute for current, unrevoked recovery authority.
            var originalProjection = JsonSerializer.Serialize(parent);
            var currentProjection = new ZipOwnershipClaimRequest(ZipOwnershipPurpose.InitialExecution, admission,
                parent.Fence, parent.OwnerUntil, "scanner-restart", profile.OwnershipLease);
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
            request.Recovery ? "scanner-restart" : "scanner-first", profile.OwnershipLease);
        await using var attempt = await ZipExecutionAttempt.ClaimAsync(ownership, grant, claim, clock);
        async Task Pause()
        {
            await using var pipe = new NamedPipeClientStream(".", request.PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(55)); await pipe.ConnectAsync(timeout.Token);
            await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync(Environment.ProcessId.ToString());
            await new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task;
        }
        var provider=new SyntheticGuardedZipScanner(profile.ScannerPolicy){Submitted=()=>f.Log("scan")};
        f.Scan=new(f.Journal,f.Journal,f.Journal,f.ChildRetention,provider);f.ComposeDriver();
        if(!request.Recovery)f.Scan.Checkpoint=name=>name==request.Boundary?Pause():Task.CompletedTask;
        var result = await f.Driver.RunScannerAsync(attempt);
        await File.WriteAllTextAsync(ResultPath(root), JsonSerializer.Serialize(new Outcome(Environment.ProcessId, grant.Purpose, result.Stop, result.Parent)));
    }
}
