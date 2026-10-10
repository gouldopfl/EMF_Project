using System.Diagnostics;
using System.Text.Json;
using EMF.Core.Contracts.Ingestion;
using EMF.Core.Contracts.Zip;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Persistence.Repositories;
using EMF.Security.Ingestion;
using EMF.Tests.TestInfrastructure;
namespace EMF.Tests;
public sealed class ZipDurableExtractionProcessRecoveryTests
{
    private sealed record Request(ArtifactId ControlArtifact,AuthenticatedIngestionOperation Authority,string Boundary,bool Recover,DateTimeOffset Now);
    private sealed record Outcome(int Process,ZipParentSnapshot Parent,string? Failure);
    private sealed class Clock(DateTimeOffset now):TimeProvider{public override DateTimeOffset GetUtcNow()=>now;}
    private static string RequestPath(string root)=>Path.Combine(root,"zip-worker-request.json");
    private static ProcessStartInfo Start(string root)
    {
        var info=new ProcessStartInfo("dotnet"){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};info.ArgumentList.Add("vstest");info.ArgumentList.Add(typeof(ZipDurableExtractionProcessRecoveryTests).Assembly.Location);
        info.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=EMF.Tests.ZipDurableExtractionProcessRecoveryTests.SyntheticZipProcessWorker");info.Environment["EMF_ZIP_SYNTHETIC_WORKER_ROOT"]=root;
        foreach(var flag in new[]{"EMF_AZURE_OPENAI_LIVE","EMF_AZURE_OPENAI_LIVE_TESTS","EMF_AZURE_MONITOR_LIVE_TESTS"})info.Environment.Remove(flag);return info;
    }
    private static async Task<int> Worker(string root,bool kill)
    {
        using var process=Process.Start(Start(root))!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();var id=process.Id;
        try
        {
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(55));
            if(kill)
            {
                while(!File.Exists(Path.Combine(root,"zip-worker-ready")))
                {if(process.HasExited)throw new InvalidOperationException("Worker exited before crash boundary: "+await output+await error);await Task.Delay(25,deadline.Token);}
                process.Kill(true);await process.WaitForExitAsync(deadline.Token);Assert.NotEqual(0,process.ExitCode);
            }
            else{await process.WaitForExitAsync(deadline.Token);Assert.True(process.ExitCode==0,await output+await error);}
            return id;
        }
        finally{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}await output;await error;}
    }
    private static async Task SeedCanonical(ArtifactIngestionFixture f,bool two)
    {
        var saved=f.SecurityContext.Operation;f.SecurityContext.Operation=new(EMF.Core.Contracts.Storage.ArtifactContentOperationId.New(),new(Guid.NewGuid().ToString("N")),"synthetic-canonical");
        var bytes="synthetic durable child payload"u8.ToArray();var draft=new IngestionMetadataDraft(new(){Id=f.Id,Name="canonical.txt",ArtifactType="file",Fingerprint=await f.Fingerprints.ComputeAsync(bytes)},new(){ArtifactId=f.Id,Source="zip-parent:parent/entry:0",RecordedBy="synthetic-canonical"});
        await f.Service().IngestAsync(draft,bytes);if(two)await f.Repository.AddProvenanceAsync(new(){ArtifactId=f.Id,Source="zip-parent:parent/entry:1",RecordedBy="synthetic-canonical"});f.SecurityContext.Operation=saved;
    }
    [Theory]
    [InlineData("A",false,false)] [InlineData("B",false,false)] [InlineData("C",false,false)] [InlineData("D",false,false)]
    [InlineData("D",true,false)] [InlineData("D-recorded",false,false)] [InlineData("D-recorded",true,false)]
    [InlineData("E",false,false)] [InlineData("E",true,false)] [InlineData("F",false,false)] [InlineData("G",false,false)] [InlineData("G",true,false)]
    [InlineData("ParentReleasePending",false,false)] [InlineData("ParentPhysicalReleased",false,false)] [InlineData("ParentCandidateDeleted",false,false)] [InlineData("ParentReleased",false,false)]
    [InlineData("A",true,true)] [InlineData("B",true,true)] [InlineData("C",true,true)] [InlineData("D",true,true)] [InlineData("E",true,true)] [InlineData("F",true,true)] [InlineData("G",true,true)]
    public async Task Actual_process_death_then_fresh_driver_reuses_all_durable_evidence(string boundary,bool dedup,bool two)
    {
        await using var evidence=await ArtifactIngestionFixture.CreateAsync();var f=new ZipDurableRuntimeFixture(evidence);await f.CreateParent(two);if(dedup)await SeedCanonical(evidence,two);
        await File.WriteAllTextAsync(Path.Combine(evidence.Root,"zip-worker-allowed"),"EMF-ZIP-SYNTHETIC-PROCESS-WORKER");var now=DateTimeOffset.UtcNow;
        await File.WriteAllTextAsync(RequestPath(evidence.Root),JsonSerializer.Serialize(new Request(evidence.Id,evidence.SecurityContext.Operation,boundary,false,now)));
        var killed=await Worker(evidence.Root,true);var crashedRuntime=int.Parse(await File.ReadAllTextAsync(Path.Combine(evidence.Root,"zip-worker-ready")));var interrupted=await f.Read();var ids=interrupted.Plan!.Entries.Select(e=>(e.ChildOperationId,e.ProvisionalArtifactId)).ToArray();var budget=interrupted.Budget;var integrity=await FirstIntegrity(evidence);
        var parentRetention=(await f.Journal.ReadParentRetentionAsync("zip"))!;var decrypt=f.Count("parent-decrypt");var childEncrypt=f.Count("child-encrypt");var ingestEncrypt=f.Count("ingestion-encrypt");var scan=f.Count("scan");
        File.Delete(evidence.SourcePath); // continuation must use private durable evidence.
        await File.WriteAllTextAsync(RequestPath(evidence.Root),JsonSerializer.Serialize(new Request(evidence.Id,evidence.SecurityContext.Operation,boundary,true,now.AddHours(1))));
        var recoveredProcess=await Worker(evidence.Root,false);var outcome=JsonSerializer.Deserialize<Outcome>(await File.ReadAllTextAsync(Path.Combine(evidence.Root,"zip-worker-result.json")))!;
        Assert.NotEqual(killed,recoveredProcess);Assert.NotEqual(crashedRuntime,outcome.Process);Assert.NotEqual(Environment.ProcessId,outcome.Process);Assert.Null(outcome.Failure);Assert.Equal(ZipParentState.Released,outcome.Parent.State);
        var final=await f.Read();Assert.Equal(ids,final.Plan!.Entries.Select(e=>(e.ChildOperationId,e.ProvisionalArtifactId)).ToArray());Assert.Equal(interrupted.Plan.Hash,final.Plan.Hash);
        Assert.Equal(two?1:0,final.Fence.ConfirmedOrdinal);Assert.Equal(two?2:1,final.Budget.ExtractionAttempts);Assert.Equal(two?2:1,final.Budget.ScannerAttempts);
        Assert.True(final.Budget.ExpandedReserved>=budget.ExpandedReserved);Assert.True(final.Budget.ExpandedProduced>=budget.ExpandedProduced);Assert.True(final.Budget.ScannerReserved>=budget.ScannerReserved);Assert.True(final.Budget.IngestionAttempts>=budget.IngestionAttempts);
        Assert.Equal(1,f.Count("parent-encrypt"));Assert.Equal(two?2:1,f.Count("child-encrypt"));Assert.Equal(two?2:1,f.Count("scan"));Assert.Equal(two?2:1,f.Count("extraction-start"));
        if(boundary=="G"||boundary.StartsWith("Parent",StringComparison.Ordinal)||(!two&&(boundary is "B" or "C" or "D" or "D-recorded" or "E" or "F"))){Assert.Equal(decrypt,f.Count("parent-decrypt"));Assert.Equal(childEncrypt,f.Count("child-encrypt"));if(boundary is not ("B" or "C"))Assert.Equal(ingestEncrypt,f.Count("ingestion-encrypt"));if(boundary!="B")Assert.Equal(scan,f.Count("scan"));}
        if(boundary=="A"||two&&(boundary is "B" or "C" or "D" or "D-recorded" or "E" or "F"))Assert.Equal(decrypt+1,f.Count("parent-decrypt"));
        if(!two&&(boundary is "B" or "C"))
        {
            Assert.Equal(budget.ExtractionAttempts,final.Budget.ExtractionAttempts);Assert.Equal(budget.ExpandedReserved,final.Budget.ExpandedReserved);Assert.Equal(budget.ExpandedProduced,final.Budget.ExpandedProduced);Assert.Equal(budget.ParentReadBytes,final.Budget.ParentReadBytes);
            Assert.Equal(interrupted.Entries[0].Retained,final.Entries[0].Retained);Assert.Equal(integrity,await FirstIntegrity(evidence));if(boundary=="C")Assert.Equal(interrupted.Entries[0].EvidenceJson,final.Entries[0].EvidenceJson);
            if(boundary=="C"){Assert.Equal(budget.ScannerAttempts,final.Budget.ScannerAttempts);Assert.Equal(budget.ScannerReserved,final.Budget.ScannerReserved);}
            else Assert.Equal(budget.ScannerAttempts+1,final.Budget.ScannerAttempts);
            Assert.Equal(budget.IngestionAttempts+1,final.Budget.IngestionAttempts);
        }
        if(boundary is "D" or "D-recorded"){Assert.Equal(ingestEncrypt+(two?1:0),f.Count("ingestion-encrypt"));Assert.True(f.Count("Resuming")>=1);}
        await Assert.ThrowsAsync<ZipFenceException>(()=>f.Journal.CompleteParentAsync(interrupted.Fence));
        var retained=(await f.Journal.ReadParentRetentionAsync("zip"))!;Assert.Equal(parentRetention.Identity,retained.Identity);Assert.Equal(parentRetention.CandidateHash,retained.CandidateHash);Assert.Equal(parentRetention.CreateReceipt,retained.CreateReceipt);Assert.Equal(ZipRetentionState.Released,retained.State);
        Assert.Equal(two?4:2,await Count(evidence,"Relationships"));Assert.Equal(two?2:1,await Count(evidence,"ZipExtractionAcknowledgements"));Assert.Equal(dedup?1:two?2:1,await Count(evidence,"Artifacts"));
        foreach(var e in final.Entries.Where(e=>!e.Plan.IsDirectory)){Assert.Equal(ZipRetentionState.Released,(await f.Journal.ReadRetentionAsync("zip",e.Plan.FileOrdinal!.Value))!.State);if(dedup)Assert.Equal(evidence.Id.Value,e.Ingestion!.CanonicalArtifactId);}
        // A third fresh process proves terminal replay performs no payload work.
        var counts=(f.Count("parent-decrypt"),f.Count("scan"),f.Count("extraction-start"),f.Count("ingestion-encrypt"));
        await File.WriteAllTextAsync(RequestPath(evidence.Root),JsonSerializer.Serialize(new Request(evidence.Id,evidence.SecurityContext.Operation,"terminal",true,now.AddHours(2))));await Worker(evidence.Root,false);
        Assert.Equal(counts,(f.Count("parent-decrypt"),f.Count("scan"),f.Count("extraction-start"),f.Count("ingestion-encrypt")));Assert.Equal(final.Budget,(await f.Read()).Budget);Assert.Equal(two?4:2,await Count(evidence,"Relationships"));
        if(boundary=="G"||boundary.StartsWith("Parent",StringComparison.Ordinal))Assert.Equal(budget,final.Budget);
        var evidenceDirectory=Path.Combine(Path.GetTempPath(),"6b-stage9-process-evidence");Directory.CreateDirectory(evidenceDirectory);
        var name=boundary+"-"+(dedup?"dedup":"adopt")+"-"+(two?"two":"one");
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory,name+".json"),JsonSerializer.Serialize(new{Boundary=boundary,Dedup=dedup,TwoEntries=two,LauncherProcess=killed,CrashedRuntimeProcess=crashedRuntime,RecoveredRuntimeProcess=outcome.Process,Interrupted=interrupted,Recovered=final,ParentBefore=parentRetention,ParentAfter=retained,IntegrityBefore=integrity,IntegrityAfter=await FirstIntegrity(evidence),DurableEvents=await File.ReadAllLinesAsync(Path.Combine(evidence.Root,"zip-events.jsonl"))},new JsonSerializerOptions{WriteIndented=true}));
    }
    private static async Task<string?> FirstIntegrity(ArtifactIngestionFixture f)
    {await using var c=new Microsoft.Data.Sqlite.SqliteConnection("Data Source="+f.DatabasePath);await c.OpenAsync();using var q=c.CreateCommand();q.CommandText="SELECT VerificationJson FROM ZipExtractionVerifications WHERE ParentOperationId='zip' AND FileOrdinal=0";return await q.ExecuteScalarAsync() as string;}
    private static async Task<long> Count(ArtifactIngestionFixture f,string table)
    {await using var c=new Microsoft.Data.Sqlite.SqliteConnection("Data Source="+f.DatabasePath);await c.OpenAsync();using var q=c.CreateCommand();q.CommandText="SELECT COUNT(*) FROM "+table;return Convert.ToInt64(await q.ExecuteScalarAsync());}
    [Fact]
    public async Task SyntheticZipProcessWorker()
    {
        var root=Environment.GetEnvironmentVariable("EMF_ZIP_SYNTHETIC_WORKER_ROOT");if(root is null)return;root=Path.GetFullPath(root);
        if(!root.StartsWith(Path.Combine(Path.GetTempPath(),"emf-m5-synthetic-"),StringComparison.Ordinal)||new DirectoryInfo(root).LinkTarget is not null||await File.ReadAllTextAsync(Path.Combine(root,"zip-worker-allowed"))!="EMF-ZIP-SYNTHETIC-PROCESS-WORKER")throw new InvalidOperationException("ZIP process worker requires explicitly marked synthetic store.");
        var request=JsonSerializer.Deserialize<Request>(await File.ReadAllTextAsync(RequestPath(root)))!;var evidence=await ArtifactIngestionFixture.OpenWorkerAsync(root,request.ControlArtifact,request.Authority);var f=new ZipDurableRuntimeFixture(evidence,new Clock(request.Now));
        async Task Pause(){var marker=Path.Combine(root,"zip-worker-ready");await File.WriteAllTextAsync(marker+".tmp",Environment.ProcessId.ToString());File.Move(marker+".tmp",marker,true);await Task.Delay(Timeout.Infinite);}
        if(!request.Recover)
        {
            f.Driver.Checkpoint=n=>n==(request.Boundary=="D-recorded"?"D":request.Boundary)?Pause():Task.CompletedTask;
            if(request.Boundary=="D")f.Ingest.Checkpoint=n=>{if(n is "Ingesting" or "Resuming")f.Log(n);return n=="CoordinatorReturned"?Pause():Task.CompletedTask;};
            if(request.Boundary.StartsWith("Parent",StringComparison.Ordinal))f.ParentRetention.Checkpoint=n=>"Parent"+n==request.Boundary?Pause():Task.CompletedTask;
            if(request.Boundary=="E")f.Journal.AcknowledgementCheckpoint=n=>{if(n=="Committed")Pause().GetAwaiter().GetResult();};
        }
        try
        {var result=await f.Driver.ResumeAsync("zip",request.Recover?"restart":"worker",TimeSpan.FromMinutes(30));await File.WriteAllTextAsync(Path.Combine(root,"zip-worker-result.json"),JsonSerializer.Serialize(new Outcome(Environment.ProcessId,result,null)));}
        catch(InvalidDataException error) when(request.Recover)
        {var current=(await f.Journal.ReadAsync("zip"))!;await File.WriteAllTextAsync(Path.Combine(root,"zip-worker-result.json"),JsonSerializer.Serialize(new Outcome(Environment.ProcessId,current,error.GetType().Name)));}
    }
}
