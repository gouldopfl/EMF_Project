using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
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
using Xunit;

namespace EMF.Tests;

[Collection(ZipProcessWideGateCollection.Name)]
public sealed class ZipProtectedReleaseProcessRecoveryTests
{
    private const string Marker="EMF-ZIP-PROTECTED-RELEASE-SYNTHETIC-WORKER";
    private const string EnvironmentName="EMF_ZIP_PROTECTED_RELEASE_SYNTHETIC_WORKER_ROOT";
    private sealed record Request(ArtifactId ControlArtifact,string OperationId,string AuthorizedOperationId,
        DateTimeOffset Now,string Boundary,string PipeName);
    private sealed record Outcome(int Process,ZipOwnershipPurpose Purpose,long ClaimedRevision,long ResultRevision,
        ZipRetentionState State,string? ReleaseRevision,ZipProtectedReleaseStop Stop);
    private sealed class Clock(DateTimeOffset now):TimeProvider
    {public override DateTimeOffset GetUtcNow()=>now;}
    private static string RequestPath(string root)=>Path.Combine(root,"zip-protected-release-worker-request.json");
    private static string ResultPath(string root)=>Path.Combine(root,"zip-protected-release-worker-result.json");
    private static string MarkerPath(string root)=>Path.Combine(root,"zip-protected-release-worker-allowed");

    private static ProcessStartInfo Start(string root)
    {
        var info=new ProcessStartInfo("dotnet")
        {UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
        info.ArgumentList.Add("vstest");
        info.ArgumentList.Add(typeof(ZipProtectedReleaseProcessRecoveryTests).Assembly.Location);
        info.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=EMF.Tests.ZipProtectedReleaseProcessRecoveryTests.SyntheticProtectedReleaseProcessWorker");
        info.Environment[EnvironmentName]=root;
        foreach(var name in new[]{"EMF_ZIP_ACK_SYNTHETIC_WORKER_ROOT","EMF_ZIP_CHILD_INGESTION_SYNTHETIC_WORKER_ROOT",
            "EMF_ZIP_SYNTHETIC_WORKER_ROOT","EMF_ZIP_PHASE_II_SYNTHETIC_WORKER_ROOT",
            "EMF_ZIP_SCANNER_SYNTHETIC_WORKER_ROOT","EMF_AZURE_OPENAI_LIVE",
            "EMF_AZURE_OPENAI_LIVE_TESTS","EMF_AZURE_MONITOR_LIVE_TESTS"})info.Environment.Remove(name);
        return info;
    }

    private static async Task<(int Process,long ClaimedRevision)> KillAtAsync(string root,string pipeName)
    {
        await using var pipe=new NamedPipeServerStream(pipeName,PipeDirection.In,1,
            PipeTransmissionMode.Byte,PipeOptions.Asynchronous);
        using var process=Process.Start(Start(root))??throw new InvalidOperationException("Unable to launch ZIP release worker.");
        var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        try
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var connection=pipe.WaitForConnectionAsync(timeout.Token);
            var exit=process.WaitForExitAsync(timeout.Token);
            if(await Task.WhenAny(connection,exit)==exit)
                throw new InvalidOperationException("ZIP release worker exited before checkpoint: "+await output+await error);
            await connection;
            using var reader=new StreamReader(pipe,leaveOpen:true);
            var message=(await reader.ReadLineAsync(timeout.Token)
                ??throw new InvalidDataException("Missing ZIP release checkpoint signal.")).Split('|');
            var pid=int.Parse(message[0]);var revision=long.Parse(message[1]);
            process.Kill(entireProcessTree:true);await exit;
            Assert.NotEqual(0,process.ExitCode);Assert.NotEqual(Environment.ProcessId,pid);
            return(pid,revision);
        }
        finally
        {
            if(!process.HasExited){process.Kill(entireProcessTree:true);await process.WaitForExitAsync();}
            await output;await error;
        }
    }

    private static async Task<Outcome> RecoverAsync(string root)
    {
        using var process=Process.Start(Start(root))??throw new InvalidOperationException("Unable to launch ZIP Recovery worker.");
        var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        try
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await process.WaitForExitAsync(timeout.Token);
            Assert.True(process.ExitCode==0,await output+await error);
            return JsonSerializer.Deserialize<Outcome>(await File.ReadAllTextAsync(ResultPath(root)))!;
        }
        finally
        {
            if(!process.HasExited){process.Kill(entireProcessTree:true);await process.WaitForExitAsync();}
            await output;await error;
        }
    }

    private static ZipDurableProfile ReadProfile(ZipParentAdmission admission)
    {
        using var document=JsonDocument.Parse(admission.Binding.ProfileJson);var p=document.RootElement;
        string Text(string name)=>p.GetProperty(name).GetString()!;
        TimeSpan Duration(string name)=>TimeSpan.FromTicks(p.GetProperty(name).GetInt64());
        var profile=new ZipDurableProfile(Text("parentNamespace"),Text("childNamespace"),
            new MalwareScannerPolicy(Text("scannerProvider"),Text("scannerVersion"),Text("scannerPolicyHash")),
            Text("protectionContract"),Duration("scannerTimeoutTicks"),Duration("ownershipLeaseTicks"),
            Duration("renewalIntervalTicks"));
        profile.ValidateRecoveryBinding(admission.Binding.ProfileJson,admission.Binding.ProfileHash);
        return profile;
    }

    private static string HashFile(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static async Task<long> DeleteReceiptCountAsync(ZipOwnershipFixture fixture,string operation)
    {
        var catalog=Path.Combine(fixture.Evidence.Root,"zip-child-private","zip-quarantine",".content-catalog.sqlite");
        await using var c=new SqliteConnection("Data Source="+catalog);await c.OpenAsync();
        using var q=c.CreateCommand();q.CommandText="SELECT COUNT(*) FROM ContentReceipts WHERE OperationId=$op";
        q.Parameters.AddWithValue("$op",operation);return Convert.ToInt64(await q.ExecuteScalarAsync());
    }

    [Theory]
    [InlineData("ReleasePending")][InlineData("PhysicalReleased")][InlineData("ReleaseReceiptRecorded")]
    public async Task Real_process_death_recovers_exact_delete_receipt_and_never_cleans_candidate(string boundary)
    {
        await using var setup=await ZipProtectedReleaseAuthorityTests.AcknowledgedAsync(two:true);
        var fixture=setup.Fixture;var root=fixture.Evidence.Root;
        var before=(await setup.Read())!;var budget=before.Budget;
        var retention=(await fixture.Journal.ReadRetentionAsync(fixture.Admission.OperationId,0))!;
        var candidate=ZipProtectedReleaseAuthorityTests.CandidatePath(fixture,retention);
        var candidateHash=HashFile(candidate);Assert.Equal(retention.CandidateHash,candidateHash);
        await setup.Attempt.DisposeAsync();await setup.Root.DisposeAsync();
        await File.WriteAllTextAsync(MarkerPath(root),Marker);
        var pipeName="emf-zip-release-"+Guid.NewGuid().ToString("N");
        var request=new Request(fixture.Evidence.Id,fixture.Admission.OperationId,
            fixture.Admission.Binding.AuthorizedOperationId,fixture.Clock.Now.AddMinutes(16),boundary,pipeName);
        await File.WriteAllTextAsync(RequestPath(root),JsonSerializer.Serialize(request));
        var (deadPid,claimedRevision)=await KillAtAsync(root,pipeName);
        var afterDeath=(await fixture.Journal.ReadRetentionAsync(request.OperationId,0))!;
        var afterDeathParent=(await fixture.Journal.ReadAsync(request.OperationId))!;
        var promoted=boundary=="ReleaseReceiptRecorded";
        Assert.Equal(ZipRetentionState.ReleasePending,afterDeath.State);
        Assert.Equal(promoted,afterDeath.ReleaseReceipt is not null);
        Assert.Null(afterDeath.CandidateCleanup);Assert.True(File.Exists(candidate));
        Assert.Equal(candidateHash,HashFile(candidate));
        Assert.Equal(ZipEntryState.Acknowledged,afterDeathParent.Entries.Single(e=>e.Plan.FileOrdinal==0).State);
        Assert.Equal(ZipEntryState.Planned,afterDeathParent.Entries.Single(e=>e.Plan.FileOrdinal==1).State);
        Assert.Equal(ZipParentState.Processing,afterDeathParent.State);
        Assert.Equal(budget,afterDeathParent.Budget);
        Assert.Equal(claimedRevision+(promoted?2:1),afterDeathParent.Fence.Revision);
        var physical=await fixture.Runtime.ChildStorage.GetReceiptAsync(new(retention.Identity.ReleaseOperationId));
        Assert.Equal(boundary!="ReleasePending",physical is {Outcome:ArtifactContentMutationOutcome.Deleted});
        Assert.Equal(boundary=="ReleasePending"?0:1,
            await DeleteReceiptCountAsync(fixture,retention.Identity.ReleaseOperationId));
        await using(var current=await fixture.Runtime.ChildStorage.ReadProtectedAsync(
            new(retention.Identity.ObjectId),retention.CreateReceipt!.CurrentRevision!.Value))
            Assert.Equal(boundary=="ReleasePending",current is not null);
        await using(var initial=new SyntheticZipExecutionGrant(fixture.Admission,ZipOwnershipPurpose.InitialExecution,fixture.Clock))
        {
            var initialRequest=await fixture.RequestAsync(initial.Purpose);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>ZipExecutionAttempt.ClaimAsync(
                new(fixture.Journal,fixture.Journal,fixture.Profile,fixture.Clock),initial,
                initialRequest,fixture.Clock));
        }
        await File.WriteAllTextAsync(RequestPath(root),JsonSerializer.Serialize(request with{Boundary="",PipeName="unused"}));
        var recovered=await RecoverAsync(root);
        Assert.NotEqual(deadPid,recovered.Process);
        Assert.Equal(ZipOwnershipPurpose.Recovery,recovered.Purpose);
        Assert.Equal(ZipProtectedReleaseStop.ReleaseReceiptRecorded,recovered.Stop);
        Assert.Equal(ZipRetentionState.ReleasePending,recovered.State);
        Assert.Equal(recovered.ClaimedRevision+(promoted?0:1),recovered.ResultRevision);
        var final=(await fixture.Journal.ReadRetentionAsync(request.OperationId,0))!;
        var finalParent=(await fixture.Journal.ReadAsync(request.OperationId))!;
        Assert.Equal(final.ReleaseReceipt,await fixture.Runtime.ChildStorage.GetReceiptAsync(new(retention.Identity.ReleaseOperationId)));
        if(physical is not null)Assert.Equal(physical,final.ReleaseReceipt);
        Assert.NotNull(final.ReleaseReceipt);Assert.Null(final.CandidateCleanup);
        Assert.Equal(final.ReleaseReceipt.CurrentRevision?.Value,recovered.ReleaseRevision);
        Assert.Equal(retention.Identity.ReleaseOperationId,final.ReleaseReceipt.OperationId.Value);
        Assert.Equal(retention.Identity.ObjectId,final.ReleaseReceipt.ArtifactId.Value);
        Assert.Equal(retention.CreateReceipt!.CurrentRevision,final.ReleaseReceipt.PriorRevision);
        Assert.Equal(retention.Revision+2,final.Revision);
        Assert.Equal(1,await DeleteReceiptCountAsync(fixture,retention.Identity.ReleaseOperationId));
        Assert.Equal(ZipRetentionState.ReleasePending,final.State);
        Assert.True(File.Exists(candidate));Assert.Equal(candidateHash,HashFile(candidate));
        Assert.Equal(budget,finalParent.Budget);
        Assert.Equal(ZipEntryState.Acknowledged,finalParent.Entries.Single(e=>e.Plan.FileOrdinal==0).State);
        Assert.Equal(ZipEntryState.Planned,finalParent.Entries.Single(e=>e.Plan.FileOrdinal==1).State);
        Assert.Equal(ZipParentState.Processing,finalParent.State);
        Assert.Equal(ZipRetentionState.Created,
            (await fixture.Journal.ReadParentRetentionAsync(request.OperationId))!.State);
        var evidenceDirectory=Path.Combine(Path.GetTempPath(),"6b-protected-release-process-evidence");
        Directory.CreateDirectory(evidenceDirectory);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory,boundary+".json"),
            JsonSerializer.Serialize(new{Boundary=boundary,DeadProcess=deadPid,
                RecoveredProcess=recovered.Process,RecoveryPurpose=recovered.Purpose,
                CandidateHashBefore=candidateHash,CandidateHashAfter=HashFile(candidate),
                RetentionBefore=retention,RetentionAfterDeath=afterDeath,RetentionFinal=final,
                ParentAfterDeath=afterDeathParent,ParentFinal=finalParent,
                ProtectedReceiptAfterDeath=physical,ProtectedReceiptFinal=final.ReleaseReceipt,
                ReleaseOperationReceiptCount=await DeleteReceiptCountAsync(fixture,retention.Identity.ReleaseOperationId)},
                new JsonSerializerOptions{WriteIndented=true}));
    }

    [Fact]
    public async Task SyntheticProtectedReleaseProcessWorker()
    {
        var root=Environment.GetEnvironmentVariable(EnvironmentName);
        if(root is null)return;
        root=Path.GetFullPath(root);
        if(!root.StartsWith(Path.Combine(Path.GetTempPath(),"emf-m5-synthetic-"),StringComparison.Ordinal)||
            new DirectoryInfo(root).LinkTarget is not null||await File.ReadAllTextAsync(MarkerPath(root))!=Marker)
            throw new InvalidOperationException("ZIP release worker requires marked synthetic storage.");
        var request=JsonSerializer.Deserialize<Request>(await File.ReadAllTextAsync(RequestPath(root)))!;
        var operation=new AuthenticatedIngestionOperation(new("synthetic-worker-unused"),
            new(request.AuthorizedOperationId),"synthetic-zip-child");
        var evidence=await ArtifactIngestionFixture.OpenWorkerAsync(root,request.ControlArtifact,operation);
        var clock=new Clock(request.Now);var runtime=new ZipDurableRuntimeFixture(evidence,clock);
        var admission=(await runtime.Journal.ReadAdmissionAsync(request.OperationId))!;
        var profile=ReadProfile(admission);
        var parent=(await runtime.Journal.ReadAsync(request.OperationId))!;
        var ownership=new ZipAuthorizedOwnershipService(runtime.Journal,runtime.Journal,profile,clock);
        await using var grant=new SyntheticZipExecutionGrant(admission,ZipOwnershipPurpose.Recovery,clock);
        var claim=new ZipOwnershipClaimRequest(ZipOwnershipPurpose.Recovery,admission,parent.Fence,parent.OwnerUntil,
            "worker",profile.OwnershipLease);
        await using var attempt=await ZipExecutionAttempt.ClaimAsync(ownership,grant,claim,clock);
        var claimed=(await runtime.Journal.ReadAsync(request.OperationId))!;
        if(request.Boundary.Length>0)
            runtime.ChildRetention.Checkpoint=point=>point==request.Boundary?PauseAsync():Task.CompletedTask;
        var result=await runtime.Driver.RunProtectedReleaseAsync(attempt);
        var retained=(await runtime.Journal.ReadRetentionAsync(request.OperationId,0))!;
        await File.WriteAllTextAsync(ResultPath(root),JsonSerializer.Serialize(new Outcome(Environment.ProcessId,
            ZipOwnershipPurpose.Recovery,claimed.Fence.Revision,result.Parent.Fence.Revision,retained.State,
            retained.ReleaseReceipt?.CurrentRevision?.Value,result.Stop)));

        async Task PauseAsync()
        {
            await using var pipe=new NamedPipeClientStream(".",request.PipeName,PipeDirection.Out,PipeOptions.Asynchronous);
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await pipe.ConnectAsync(timeout.Token);
            await using var writer=new StreamWriter(pipe,leaveOpen:true){AutoFlush=true};
            await writer.WriteLineAsync(Environment.ProcessId+"|"+claimed.Fence.Revision);
            await new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task;
        }
    }
}
