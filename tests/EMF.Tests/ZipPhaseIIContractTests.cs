using EMF.Core.Contracts.Zip;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Services;
using EMF.Tests.TestInfrastructure;
namespace EMF.Tests;

public sealed class ZipPhaseIIContractTests
{
    [Fact]
    public async Task Null_attempt_cannot_select_internal_compatibility_paths()
    {
        await using var f=await ZipOwnershipFixture.CreateAsync();
        var parent=(await f.Journal.ReadAsync(f.Admission.OperationId))!;
        var before=parent.Budget;
        using var lease=new EMF.Core.Contracts.Storage.ArtifactContentReadLease(new(parent.Binding.Input.ContentId),
            new(parent.Binding.Input.Revision),0,[]);
        await Assert.ThrowsAsync<ArgumentNullException>(()=>f.Runtime.ParentRetention.OpenAsync(parent,null!));
        await Assert.ThrowsAsync<ArgumentNullException>(()=>new ZipEntryPlanner().AdmitAsync(f.Journal,parent,lease,null!));
        await Assert.ThrowsAsync<ArgumentNullException>(()=>f.Runtime.Extractor.MaterializeNextAsync(parent,lease,null!));
        await Assert.ThrowsAsync<ArgumentNullException>(()=>f.Runtime.ChildRetention.SealAsync(parent,0,null,null!));
        Assert.Equal(before,(await f.Journal.ReadAsync(f.Admission.OperationId))!.Budget);
    }
    [Fact]
    public void Covered_payload_services_expose_no_public_raw_compatibility_overload()
    {
        foreach(var type in new[]{typeof(ZipEntryPlanner),typeof(ZipSequentialExtractor),typeof(ZipParentRetentionService)})
        {
            foreach(var method in type.GetMethods().Where(m=>m.Name is "AdmitAsync" or "MaterializeNextAsync" or "OpenAsync" or "RetainAsync" or "RecoverCreationAsync"))
                Assert.Contains(method.GetParameters(),p=>p.ParameterType==typeof(ZipExecutionAttempt));
        }
        Assert.All(typeof(ZipProtectedRetentionService).GetMethods().Where(m=>m.Name=="SealAsync"),
            method=>Assert.Contains(method.GetParameters(),p=>p.ParameterType==typeof(ZipExecutionAttempt)));
    }
    [Fact]
    public async Task Fresh_root_is_required_for_a_second_execution_attempt()
    {
        await using var f=await ZipOwnershipFixture.CreateAsync();
        var service=new ZipAuthorizedOwnershipService(f.Journal,f.Journal,f.Profile,f.Clock);
        await using var first=new SyntheticZipExecutionGrant(f.Admission,ZipOwnershipPurpose.InitialExecution,f.Clock);
        await using(var attempt=await ZipExecutionAttempt.ClaimAsync(service,first,await f.RequestAsync(first.Purpose),f.Clock)){}
        await using var recovery=new SyntheticZipExecutionGrant(f.Admission,ZipOwnershipPurpose.Recovery,f.Clock);
        await using(var attempt=await ZipExecutionAttempt.ClaimAsync(service,recovery,await f.RequestAsync(recovery.Purpose),f.Clock)){}
        var before=(await f.Journal.ReadAsync(f.Admission.OperationId))!;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>ZipExecutionAttempt.ClaimAsync(service,recovery,
            new(ZipOwnershipPurpose.Recovery,f.Admission,before.Fence,before.OwnerUntil,"worker",f.Profile.OwnershipLease),f.Clock));
        Assert.Equal(before.Fence,(await f.Journal.ReadAsync(f.Admission.OperationId))!.Fence);
    }
    [Fact]
    public async Task Async_issuer_and_scope_disposal_do_not_deadlock_reentrant_metered_reads()
    {
        await using var f=await ZipOwnershipFixture.CreateAsync();
        await using var grant=new SyntheticZipExecutionGrant(f.Admission,ZipOwnershipPurpose.InitialExecution,f.Clock)
        {
            BeforeRevalidation=async()=>await Task.Yield(),
            BeforeEffectDisposal=async()=>await Task.Yield()
        };
        grant.OnEffectRequest=request=>Assert.True(request is ZipParentReadRequest or ZipPlanAdmissionRequest or ZipChildExtractionRequest or ZipChildRetentionRequest);
        var service=new ZipAuthorizedOwnershipService(f.Journal,f.Journal,f.Profile,f.Clock);
        await using var attempt=await ZipExecutionAttempt.ClaimAsync(service,grant,await f.RequestAsync(grant.Purpose),f.Clock);
        var result=await f.Runtime.Driver.RunAsync(attempt).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(ZipPhaseIIStop.AwaitingScanner,result.Stop);
        Assert.Equal(0,f.Runtime.Count("scan"));
        Assert.True(grant.EffectRequests>0);
    }
    [Fact]
    public async Task Two_file_archive_stops_after_only_the_next_unconfirmed_occurrence()
    {
        await using var f=await ZipOwnershipFixture.CreateAsync(two:true);
        await using var grant=new SyntheticZipExecutionGrant(f.Admission,ZipOwnershipPurpose.InitialExecution,f.Clock);
        var service=new ZipAuthorizedOwnershipService(f.Journal,f.Journal,f.Profile,f.Clock);
        await using var attempt=await ZipExecutionAttempt.ClaimAsync(service,grant,await f.RequestAsync(grant.Purpose),f.Clock);
        var result=await f.Runtime.Driver.RunAsync(attempt);
        Assert.Equal(ZipPhaseIIStop.AwaitingScanner,result.Stop);
        Assert.Equal(ZipEntryState.Materialized,result.Parent.Entries.Single(e=>e.Plan.FileOrdinal==0).State);
        Assert.Equal(ZipEntryState.Planned,result.Parent.Entries.Single(e=>e.Plan.FileOrdinal==1).State);
        Assert.Null(await f.Journal.ReadRetentionAsync(f.Admission.OperationId,1));
        Assert.Equal(1,result.Parent.Budget.ExtractionAttempts);
        Assert.Equal(-1,result.Parent.Fence.ConfirmedOrdinal);
        Assert.Equal(0,result.Parent.Budget.ScannerAttempts);
        Assert.Equal(0,result.Parent.Budget.IngestionAttempts);
    }
    [Fact]
    public async Task Concurrency_renewal_cannot_extend_original_root_deadline()
    {
        await using var f=await ZipOwnershipFixture.CreateAsync();
        await using var grant=new SyntheticZipExecutionGrant(f.Admission,ZipOwnershipPurpose.InitialExecution,f.Clock,TimeSpan.FromSeconds(90));
        var service=new ZipAuthorizedOwnershipService(f.Journal,f.Journal,f.Profile,f.Clock);
        await using var attempt=await ZipExecutionAttempt.ClaimAsync(service,grant,await f.RequestAsync(grant.Purpose),f.Clock);
        var p=attempt.Claimed;
        // Keep the concurrency lease continuously current without payload work.
        while(f.Clock.Now<grant.ValidUntilUtc)
        {
            f.Clock.Now=f.Clock.Now.AddSeconds(30);
            var until=await f.Journal.RenewOwnershipAsync(p.Fence.OperationId,p.Fence.Owner,p.Fence.Epoch,f.Profile.OwnershipLease);
            p=p with {OwnerUntil=until};
        }
        Assert.True(p.OwnerUntil>f.Clock.Now);
        Assert.Equal(grant.ValidUntilUtc,attempt.ValidUntilUtc);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Runtime.ParentRetention.OpenAsync(p,attempt));
        Assert.Equal(p.Budget,(await f.Journal.ReadAsync(f.Admission.OperationId))!.Budget);
    }
    [Theory]
    [InlineData("root")][InlineData("actor")][InlineData("request")][InlineData("deadline")]
    public async Task Scope_anchor_mutation_between_revalidation_and_acceptance_denies_effect(string changed)
    {
        var root=new object();var now=TimeProvider.System.GetUtcNow();
        var scope=new Scope(root,"actor",now.AddMinutes(5));var invalidated=false;
        var boundary=new ZipPhaseIIBoundary<string>(root,"actor",now.AddMinutes(5),TimeProvider.System,
            CancellationToken.None,_=>Task.CompletedTask,()=>{},()=>invalidated=true,()=>"exact",(_,_)=>Task.FromResult<IZipAuthorizedEffectScope<string>>(scope));
        await boundary.RevalidateAsync();
        switch(changed){case "root":scope.Root=new();break;case "actor":scope.ExecutingActorId="other";break;
            case "request":scope.Request="other";break;case "deadline":scope.ValidUntilUtc=now.AddMinutes(6);break;}
        Assert.Throws<UnauthorizedAccessException>(()=>boundary.Accept());
        Assert.True(invalidated);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>boundary.DisposeAsync().AsTask());
        Assert.Equal(1,scope.Disposals);
    }
    private sealed class Scope(object root,string actor,DateTimeOffset until):IZipAuthorizedEffectScope<string>
    {
        public object Root{get;set;}=root;public string Request{get;set;}="exact";
        public string ExecutingActorId{get;set;}=actor;public DateTimeOffset ValidUntilUtc{get;set;}=until;
        public CancellationToken Invalidated=>CancellationToken.None;
        public ValueTask RevalidateAsync(CancellationToken ct=default)=>ValueTask.CompletedTask;
        public IDisposable Accept(CancellationToken ct=default)=>new Acceptance();
        internal int Disposals;
        public ValueTask DisposeAsync(){Disposals++;return ValueTask.CompletedTask;}
        private sealed class Acceptance:IDisposable{public void Dispose(){}}
    }
}
