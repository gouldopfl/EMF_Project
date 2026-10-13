using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using EMF.Core.Contracts.Malware;
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
public sealed class ZipAcknowledgementProcessRecoveryTests
{
    private sealed record Request(ArtifactId ControlArtifact,string OperationId,string AuthorizedOperationId,
        DateTimeOffset Now,string Boundary,string PipeName);
    private sealed record Outcome(int Process,ZipOwnershipPurpose Purpose,ZipAcknowledgementStop Stop,
        bool AlreadyAcknowledged,long? ContainsId,long? DerivedFromId,long ClaimedRevision,
        long ResultRevision,int ConfirmedOrdinal);
    private sealed class Clock(DateTimeOffset now):TimeProvider
    { public override DateTimeOffset GetUtcNow()=>now; }

    private const string Marker="EMF-ZIP-ACK-SYNTHETIC-PROCESS-WORKER";
    private const string EnvironmentName="EMF_ZIP_ACK_SYNTHETIC_WORKER_ROOT";
    private static string RequestPath(string root)=>Path.Combine(root,"zip-ack-worker-request.json");
    private static string ResultPath(string root)=>Path.Combine(root,"zip-ack-worker-result.json");
    private static string MarkerPath(string root)=>Path.Combine(root,"zip-ack-worker-allowed");

    private static ProcessStartInfo Start(string root)
    {
        var info=new ProcessStartInfo("dotnet")
        {UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
        info.ArgumentList.Add("vstest");
        info.ArgumentList.Add(typeof(ZipAcknowledgementProcessRecoveryTests).Assembly.Location);
        info.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=EMF.Tests.ZipAcknowledgementProcessRecoveryTests.SyntheticAckProcessWorker");
        info.Environment[EnvironmentName]=root;
        foreach(var name in new[]{"EMF_ZIP_CHILD_INGESTION_SYNTHETIC_WORKER_ROOT",
            "EMF_ZIP_SYNTHETIC_WORKER_ROOT","EMF_ZIP_PHASE_II_SYNTHETIC_WORKER_ROOT",
            "EMF_ZIP_SCANNER_SYNTHETIC_WORKER_ROOT","EMF_AZURE_OPENAI_LIVE",
            "EMF_AZURE_OPENAI_LIVE_TESTS","EMF_AZURE_MONITOR_LIVE_TESTS"})info.Environment.Remove(name);
        return info;
    }

    private static async Task<(int Pid,long ClaimedRevision)> KillAtAsync(string root,string pipeName)
    {
        await using var pipe=new NamedPipeServerStream(pipeName,PipeDirection.In,1,
            PipeTransmissionMode.Byte,PipeOptions.Asynchronous);
        using var process=Process.Start(Start(root))??throw new InvalidOperationException("Unable to launch ACK worker.");
        var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        try
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(55));
            var connection=pipe.WaitForConnectionAsync(timeout.Token);
            var exit=process.WaitForExitAsync(timeout.Token);
            if(await Task.WhenAny(connection,exit)==exit)
                throw new InvalidOperationException("ACK worker exited before checkpoint: "+await output+await error);
            await connection;
            using var reader=new StreamReader(pipe,leaveOpen:true);
            var message=(await reader.ReadLineAsync(timeout.Token)
                ??throw new InvalidDataException("Missing ACK checkpoint signal.")).Split('|');
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
        using var process=Process.Start(Start(root))??throw new InvalidOperationException("Unable to launch ACK recovery worker.");
        var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        try
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(55));
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

    private static async Task<long> CountAsync(string path,string table)
    {
        await using var c=new SqliteConnection("Data Source="+path);await c.OpenAsync();using var q=c.CreateCommand();
        q.CommandText=table switch
        {
            "Relationships"=>"SELECT COUNT(*) FROM Relationships",
            "ACK"=>"SELECT COUNT(*) FROM ZipExtractionAcknowledgements",
            "Delivered"=>"SELECT COUNT(*) FROM ArtifactIngestionAudit WHERE Delivered=1",
            _=>throw new ArgumentOutOfRangeException(nameof(table))
        };
        return Convert.ToInt64(await q.ExecuteScalarAsync());
    }

    private static async Task<(long Contains,long Derived)?> OwnedIdsAsync(string path)
    {
        await using var c=new SqliteConnection("Data Source="+path);await c.OpenAsync();using var q=c.CreateCommand();
        q.CommandText="SELECT ContainsId,DerivedFromId FROM ZipExtractionAcknowledgements";
        using var row=await q.ExecuteReaderAsync();
        if(!await row.ReadAsync())return null;
        var ids=(row.GetInt64(0),row.GetInt64(1));
        Assert.False(await row.ReadAsync());
        return ids;
    }

    private static ZipDurableProfile ReadProfile(ZipParentAdmission admission)
    {
        using var document=JsonDocument.Parse(admission.Binding.ProfileJson);var p=document.RootElement;
        string Text(string name)=>p.GetProperty(name).GetString()!;
        TimeSpan Duration(string name)=>TimeSpan.FromTicks(p.GetProperty(name).GetInt64());
        var profile=new ZipDurableProfile(Text("parentNamespace"),Text("childNamespace"),
            new MalwareScannerPolicy(Text("scannerProvider"),Text("scannerVersion"),Text("scannerPolicyHash")),
            Text("protectionContract"),Duration("scannerTimeoutTicks"),
            Duration("ownershipLeaseTicks"),Duration("renewalIntervalTicks"));
        profile.ValidateRecoveryBinding(admission.Binding.ProfileJson,admission.Binding.ProfileHash);
        return profile;
    }

    [Theory]
    [InlineData("Validated")]
    [InlineData("CommitWindow")]
    [InlineData("Committed")]
    public async Task Real_process_death_preserves_atomic_ack_and_fresh_recovery_reuses_owned_ids(string boundary)
    {
        await using var setup=await ZipScannerAuthorityTests.Setup.Create();
        await setup.Scan();
        setup.Root.GuardedChildOpener=(p,e,gate,ct)=>setup.Fixture.Runtime.Host.OpenGuardedAsync(p,e,gate,ct);
        Assert.Equal(ZipChildIngestionStop.Ingested,
            (await setup.Fixture.Runtime.Driver.RunChildIngestionAsync(setup.Attempt)).Stop);
        var fixture=setup.Fixture;var root=fixture.Evidence.Root;
        var ingested=(await fixture.Journal.ReadAsync(fixture.Admission.OperationId))!;
        var budget=ingested.Budget;var delivered=await CountAsync(fixture.Evidence.DatabasePath,"Delivered");
        await setup.Attempt.DisposeAsync();await setup.Root.DisposeAsync();
        await File.WriteAllTextAsync(MarkerPath(root),Marker);
        var pipeName="emf-zip-ack-"+Guid.NewGuid().ToString("N");
        var request=new Request(fixture.Evidence.Id,fixture.Admission.OperationId,
            fixture.Admission.Binding.AuthorizedOperationId,fixture.Clock.Now.AddMinutes(16),boundary,pipeName);
        await File.WriteAllTextAsync(RequestPath(root),JsonSerializer.Serialize(request));
        var (deadPid,claimedRevision)=await KillAtAsync(root,pipeName);
        var afterDeath=(await fixture.Journal.ReadAsync(request.OperationId))!;
        var committed=boundary=="Committed";
        Assert.Equal(budget,afterDeath.Budget);
        Assert.Equal(delivered,await CountAsync(fixture.Evidence.DatabasePath,"Delivered"));
        Assert.Equal(committed?ZipEntryState.Acknowledged:ZipEntryState.Ingested,afterDeath.Entries.Single().State);
        Assert.Equal(committed?0:-1,afterDeath.Fence.ConfirmedOrdinal);
        Assert.Equal(claimedRevision+(committed?1:0),afterDeath.Fence.Revision);
        Assert.Equal(committed?1:0,await CountAsync(fixture.Evidence.DatabasePath,"ACK"));
        Assert.Equal(committed?2:0,await CountAsync(fixture.Evidence.DatabasePath,"Relationships"));
        var committedIds=await OwnedIdsAsync(fixture.Evidence.DatabasePath);
        if(committed)
        {
            Assert.NotNull(committedIds);
            Assert.True(committedIds.Value.Contains>0);
            Assert.True(committedIds.Value.Derived>0);
            Assert.NotEqual(committedIds.Value.Contains,committedIds.Value.Derived);
        }
        else Assert.Null(committedIds);

        // A fresh InitialExecution is forbidden even after the precommit death.
        await using(var forbidden=new SyntheticZipExecutionGrant(fixture.Admission,ZipOwnershipPurpose.InitialExecution,fixture.Clock))
        {
            var claim=await fixture.RequestAsync(forbidden.Purpose);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>ZipExecutionAttempt.ClaimAsync(
                new(fixture.Journal,fixture.Journal,fixture.Profile,fixture.Clock),forbidden,claim,fixture.Clock));
        }
        Assert.Equal(afterDeath.Fence,(await fixture.Journal.ReadAsync(request.OperationId))!.Fence);
        await File.WriteAllTextAsync(RequestPath(root),JsonSerializer.Serialize(request with{Boundary="",PipeName="unused"}));
        var recovered=await RecoverAsync(root);
        Assert.NotEqual(deadPid,recovered.Process);
        Assert.Equal(ZipOwnershipPurpose.Recovery,recovered.Purpose);
        Assert.Equal(ZipAcknowledgementStop.Acknowledged,recovered.Stop);
        Assert.Equal(committed,recovered.AlreadyAcknowledged);
        Assert.Equal(recovered.ClaimedRevision+(committed?0:1),recovered.ResultRevision);
        Assert.Equal(0,recovered.ConfirmedOrdinal);
        var final=(await fixture.Journal.ReadAsync(request.OperationId))!;
        Assert.Equal(ZipEntryState.Acknowledged,final.Entries.Single().State);
        Assert.Equal(recovered.ResultRevision,final.Fence.Revision);
        Assert.Equal(budget,final.Budget);
        Assert.Equal(1,await CountAsync(fixture.Evidence.DatabasePath,"ACK"));
        Assert.Equal(2,await CountAsync(fixture.Evidence.DatabasePath,"Relationships"));
        if(committed)Assert.Equal(committedIds,await OwnedIdsAsync(fixture.Evidence.DatabasePath));
        Assert.Equal(delivered,await CountAsync(fixture.Evidence.DatabasePath,"Delivered"));
        var receipt=(await fixture.Journal.AcknowledgeEntryAsync(final.Fence,0,final.Entries.Single().Ingestion!));
        Assert.True(receipt.AlreadyAcknowledged);
        Assert.Equal(receipt.ContainsId,recovered.ContainsId);
        Assert.Equal(receipt.DerivedFromId,recovered.DerivedFromId);
        Assert.Equal(ZipRetentionState.Created,(await fixture.Journal.ReadRetentionAsync(request.OperationId,0))!.State);
    }

    [Fact]
    public async Task SyntheticAckProcessWorker()
    {
        var root=Environment.GetEnvironmentVariable(EnvironmentName);
        if(root is null)return;
        root=Path.GetFullPath(root);
        if(!root.StartsWith(Path.Combine(Path.GetTempPath(),"emf-m5-synthetic-"),StringComparison.Ordinal)||
            new DirectoryInfo(root).LinkTarget is not null||await File.ReadAllTextAsync(MarkerPath(root))!=Marker)
            throw new InvalidOperationException("ACK worker requires explicitly marked synthetic storage.");
        var request=JsonSerializer.Deserialize<Request>(await File.ReadAllTextAsync(RequestPath(root)))!;
        var operation=new AuthenticatedIngestionOperation(new("synthetic-worker-unused"),
            new(request.AuthorizedOperationId),"synthetic-zip-child");
        var evidence=await ArtifactIngestionFixture.OpenWorkerAsync(root,request.ControlArtifact,operation);
        var clock=new Clock(request.Now);var runtime=new ZipDurableRuntimeFixture(evidence,clock);
        var admission=(await runtime.Journal.ReadAdmissionAsync(request.OperationId))!;
        var profile=ReadProfile(admission);
        var parent=(await runtime.Journal.ReadAsync(request.OperationId))!;
        var ownership=new ZipAuthorizedOwnershipService(runtime.Journal,runtime.Journal,profile,clock);
        var purpose=ZipOwnershipPurpose.Recovery;
        await using var grant=new SyntheticZipExecutionGrant(admission,purpose,clock);
        var claim=new ZipOwnershipClaimRequest(purpose,admission,parent.Fence,parent.OwnerUntil,
            "worker",profile.OwnershipLease);
        await using var attempt=await ZipExecutionAttempt.ClaimAsync(ownership,grant,claim,clock);
        var claimed=(await runtime.Journal.ReadAsync(request.OperationId))!;
        if(request.Boundary.Length>0)
            runtime.Journal.AcknowledgementCheckpoint=point=>
            {
                if(point==request.Boundary)PauseAsync().GetAwaiter().GetResult();
            };
        var result=await runtime.Driver.RunAcknowledgementAsync(attempt);
        await File.WriteAllTextAsync(ResultPath(root),JsonSerializer.Serialize(new Outcome(
            Environment.ProcessId,purpose,result.Stop,result.AlreadyAcknowledged,result.ContainsId,
            result.DerivedFromId,claimed.Fence.Revision,result.Parent.Fence.Revision,result.Parent.Fence.ConfirmedOrdinal)));

        async Task PauseAsync()
        {
            await using var pipe=new NamedPipeClientStream(".",request.PipeName,PipeDirection.Out,PipeOptions.Asynchronous);
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(55));
            await pipe.ConnectAsync(timeout.Token);
            await using var writer=new StreamWriter(pipe,leaveOpen:true){AutoFlush=true};
            await writer.WriteLineAsync(Environment.ProcessId+"|"+claimed.Fence.Revision);
            await new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task;
        }
    }
}
