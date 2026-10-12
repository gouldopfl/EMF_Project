using EMF.Core.Contracts.Zip;
using EMF.Tests.TestInfrastructure;
namespace EMF.Tests;
[Collection(ZipProcessWideGateCollection.Name)]
public sealed class ZipPhaseIIAdmissionEligibilityTests
{
    private sealed class ValidGuard : IZipEffectBoundaryGuard
    {
        public CancellationToken Invalidated => CancellationToken.None;
        public ValueTask RevalidateAsync(CancellationToken ct=default)=>ValueTask.CompletedTask;
        public IDisposable Accept(CancellationToken ct=default)=>new Acceptance();
        private sealed class Acceptance:IDisposable {public void Dispose(){}}
    }
    [Theory]
    [InlineData(ZipAdmissionEventKind.Revoked)]
    [InlineData(ZipAdmissionEventKind.RequiresReview)]
    public async Task Current_permission_cannot_advance_stopped_admission(ZipAdmissionEventKind kind)
    {
        await using var f=await ZipOwnershipFixture.CreateAsync();
        var p=await f.Journal.ClaimOwnershipAsync(await f.RequestAsync(ZipOwnershipPurpose.InitialExecution));
        var stopped=await f.Journal.AppendAdmissionEventAsync(f.Admission,
            new("diagnostic-stop",kind,"synthetic-actor",f.Clock.Now,SafeReason:"DiagnosticStop"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Journal.ChargeParentReadAsync(p.Fence,new ValidGuard()));
        var after=(await f.Journal.ReadAsync(p.Binding.OperationId))!;
        Assert.Equal(p.Fence,after.Fence);Assert.Equal(p.Budget,after.Budget);
        Assert.Equal(stopped,await f.Journal.ReadAdmissionAsync(p.Binding.OperationId));
    }

    [Theory]
    [InlineData("ParentOpened", ZipAdmissionEventKind.Revoked)]
    [InlineData("ParentOpened", ZipAdmissionEventKind.RequiresReview)]
    [InlineData("IntegrityPersisted", ZipAdmissionEventKind.Revoked)]
    [InlineData("IntegrityPersisted", ZipAdmissionEventKind.RequiresReview)]
    public async Task Durable_stop_vetoes_driver_progression_even_when_live_root_is_valid(string boundary, ZipAdmissionEventKind kind)
    {
        await using var f=await ZipOwnershipFixture.CreateAsync();
        await using var grant=new SyntheticZipExecutionGrant(f.Admission,ZipOwnershipPurpose.InitialExecution,f.Clock);
        await using var attempt=await EMF.Orchestration.Services.ZipExecutionAttempt.ClaimAsync(
            new(f.Journal,f.Journal,f.Profile,f.Clock),grant,await f.RequestAsync(grant.Purpose),f.Clock);
        ZipParentSnapshot? before=null;
        ZipParentAdmission? stopped=null;
        async Task Checkpoint(string point)
        {
            if(point!=boundary)return;
            before=(await f.Journal.ReadAsync(f.Admission.OperationId))!;
            stopped=await f.Journal.AppendAdmissionEventAsync(f.Admission,
                new("durable-stop",kind,"synthetic-actor",f.Clock.Now,SafeReason:"DurableStop"));
        }
        f.Runtime.Driver.Checkpoint=Checkpoint;
        f.Runtime.Extractor.Checkpoint=Checkpoint;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Runtime.Driver.RunAsync(attempt));
        Assert.NotNull(before);Assert.NotNull(stopped);
        Assert.False(grant.Revoked.IsCancellationRequested);
        await grant.RevalidateAsync();
        var after=(await f.Journal.ReadAsync(f.Admission.OperationId))!;
        Assert.Equal(before!.Fence,after.Fence);Assert.Equal(before.Budget,after.Budget);
        Assert.Equal(stopped,await f.Journal.ReadAdmissionAsync(f.Admission.OperationId));
        Assert.Null(await f.Journal.ReadRetentionAsync(f.Admission.OperationId,0));
        Assert.Equal(0,f.Runtime.Count("child-encrypt"));Assert.Equal(0,f.Runtime.Count("scan"));
        Assert.Equal(0,f.Runtime.Count("ingestion-encrypt"));Assert.Equal(-1,after.Fence.ConfirmedOrdinal);
        Assert.Equal(0,after.Budget.ScannerAttempts);Assert.Equal(0,after.Budget.IngestionAttempts);
    }
}
