using System.Security.Cryptography;
using EMF.Core.Contracts.Malware;
using EMF.Core.Contracts.Zip;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

[Collection(ZipProcessWideGateCollection.Name)]
public sealed class ZipScannerResponseOwnershipTests
{
    private sealed class ForeignResponse(IZipScannerResponse inner,ZipScannerInvocation other):IZipScannerResponse
    {
        internal bool Closed,Received;public ZipScannerInvocation Invocation=>other;
        public Task<ZipCompletedScanObservation> ReceiveObservationAsync(CancellationToken hardAbort=default)
        {Received=true;return inner.ReceiveObservationAsync(hardAbort);}
        public async ValueTask DisposeAsync(){Closed=true;await inner.DisposeAsync();}
    }
    [Fact]
    public async Task Wrong_invocation_response_is_denied_and_closed_without_receive_send_or_progression()
    {
        await using var s=await ZipScannerAuthorityTests.Setup.Create();ForeignResponse? foreign=null;
        s.Provider.TransformSubmission=outcome=>
        {
            var actual=outcome.Response!;var other=s.Provider.CreateInvocation(actual.Invocation.Attempt,s.Fixture.Clock.Now,s.Fixture.Clock);
            foreign=new(actual,other);return Task.FromResult(new ZipScannerSubmission(null,foreign));
        };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>s.Scan());Assert.True(foreign!.Closed);Assert.False(foreign.Received);
        Assert.True(s.Provider.ResponseClosed);Assert.All(s.Provider.LastBuffer!,b=>Assert.Equal(0,b));Assert.All(s.Fixture.Runtime.ChildCrypto.LastPlaintext!,b=>Assert.Equal(0,b));
        var p=(await s.Read())!;Assert.Null(p.Entries[0].EvidenceJson);Assert.Equal(ZipEntryState.Materialized,p.Entries[0].State);
        Assert.Equal(1,s.Provider.Calls["Payload"]);ZipScannerAuthorityTests.NoDownstream(s.Fixture,p);
    }
    [Fact]
    public async Task Ambiguous_response_is_closed_and_only_exact_already_sealed_observation_is_preserved()
    {
        await using var s=await ZipScannerAuthorityTests.Setup.Create();ZipCompletedScanObservation? exact=null;
        s.Provider.TransformSubmission=async outcome=>{exact=await outcome.Response!.ReceiveObservationAsync();return new(exact,outcome.Response);};
        await Assert.ThrowsAsync<InvalidDataException>(()=>s.Scan());Assert.True(s.Provider.ResponseClosed);var p=(await s.Read())!;
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(exact!.Evidence),p.Entries[0].EvidenceJson);
        Assert.Equal(ZipEntryState.Materialized,p.Entries[0].State);Assert.Equal(1,s.Provider.Calls["Payload"]);
        Assert.All(s.Provider.LastBuffer!,b=>Assert.Equal(0,b));Assert.All(s.Fixture.Runtime.ChildCrypto.LastPlaintext!,b=>Assert.Equal(0,b));ZipScannerAuthorityTests.NoDownstream(s.Fixture,p);
    }
    [Theory]
    [InlineData("Submitted")][InlineData("PlaintextCleared")]
    public async Task Exception_after_submission_closes_connection_clears_plaintext_and_preserves_sealed_fact_without_handoff(string boundary)
    {
        await using var s=await ZipScannerAuthorityTests.Setup.Create();s.Fixture.Runtime.Scan.Checkpoint=point=>
        {if(point==boundary)throw new IOException("Deterministic post-submission exception.");return Task.CompletedTask;};
        await Assert.ThrowsAsync<IOException>(()=>s.Scan());Assert.True(s.Provider.ResponseClosed);var p=(await s.Read())!;
        Assert.NotNull(p.Entries[0].EvidenceJson);Assert.Equal(ZipEntryState.Materialized,p.Entries[0].State);Assert.Equal(1,p.Budget.ScannerAttempts);
        Assert.Equal(1,s.Provider.Calls["Payload"]);Assert.All(s.Provider.LastBuffer!,b=>Assert.Equal(0,b));Assert.All(s.Fixture.Runtime.ChildCrypto.LastPlaintext!,b=>Assert.Equal(0,b));
        ZipScannerAuthorityTests.NoDownstream(s.Fixture,p);
    }
    [Fact]
    public async Task Disposal_failure_after_sealing_closes_connection_preserves_fact_and_denies_execution_handoff()
    {
        await using var s=await ZipScannerAuthorityTests.Setup.Create();s.Provider.BeforeDispose=()=>throw new IOException("Deterministic disposal failure after close.");
        await Assert.ThrowsAsync<IOException>(()=>s.Scan());Assert.True(s.Provider.ResponseClosed);var p=(await s.Read())!;
        Assert.NotNull(p.Entries[0].EvidenceJson);Assert.Equal(ZipEntryState.Materialized,p.Entries[0].State);Assert.Equal(1,s.Provider.Calls["Payload"]);
        Assert.All(s.Provider.LastBuffer!,b=>Assert.Equal(0,b));Assert.All(s.Fixture.Runtime.ChildCrypto.LastPlaintext!,b=>Assert.Equal(0,b));ZipScannerAuthorityTests.NoDownstream(s.Fixture,p);
        s.Provider.BeforeDispose=null;Assert.Equal(EMF.Orchestration.Services.ZipScannerStop.AwaitingChildIngestion,(await s.Recover()).Stop);
    }
    [Fact]
    public async Task Revocation_after_sealing_and_during_disposal_preserves_fact_without_additional_send_or_progression()
    {
        await using var s=await ZipScannerAuthorityTests.Setup.Create();var arrived=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);s.Provider.BeforeDispose=()=>{arrived.TrySetResult();return release.Task;};
        var run=s.Scan();await arrived.Task.WaitAsync(TimeSpan.FromSeconds(10));var sends=s.Provider.Calls["Payload"];s.Root.Revoke();release.SetResult();
        await ZipScannerAuthorityTests.Denied(run);var p=(await s.Read())!;
        Assert.True(s.Provider.ResponseClosed);Assert.NotNull(p.Entries[0].EvidenceJson);Assert.Equal(ZipEntryState.Materialized,p.Entries[0].State);
        Assert.Equal(sends,s.Provider.Calls["Payload"]);Assert.All(s.Provider.LastBuffer!,b=>Assert.Equal(0,b));ZipScannerAuthorityTests.NoDownstream(s.Fixture,p);
    }
    [Fact]
    public async Task Submission_throw_after_private_seal_preserves_that_exact_fact_and_closes_resources_without_progression()
    {
        await using var s=await ZipScannerAuthorityTests.Setup.Create();ZipCompletedScanObservation? sealedFact=null;
        s.Provider.BeforeDispose=()=>throw new IOException("Submission cleanup failed after seal.");
        s.Provider.TransformSubmission=async outcome=>
        {sealedFact=await outcome.Response!.ReceiveObservationAsync();await outcome.Response.DisposeAsync();return outcome;};
        await Assert.ThrowsAsync<IOException>(()=>s.Scan());var p=(await s.Read())!;
        Assert.True(s.Provider.ResponseClosed);Assert.NotNull(sealedFact);Assert.Equal(System.Text.Json.JsonSerializer.Serialize(sealedFact!.Evidence),p.Entries[0].EvidenceJson);
        Assert.Equal(ZipEntryState.Materialized,p.Entries[0].State);Assert.Equal(1,s.Provider.Calls["Payload"]);Assert.Equal(1,p.Budget.ScannerAttempts);
        Assert.All(s.Provider.LastBuffer!,b=>Assert.Equal(0,b));Assert.All(s.Fixture.Runtime.ChildCrypto.LastPlaintext!,b=>Assert.Equal(0,b));ZipScannerAuthorityTests.NoDownstream(s.Fixture,p);
    }

    private sealed class ThrowAfterSeal(IZipScannerResponse inner):IZipScannerResponse
    {
        public ZipScannerInvocation Invocation=>inner.Invocation;
        public async Task<ZipCompletedScanObservation> ReceiveObservationAsync(CancellationToken hardAbort=default)
        {await inner.ReceiveObservationAsync(hardAbort);throw new IOException("Custom response failed after its exact observation was sealed.");}
        public ValueTask DisposeAsync()=>inner.DisposeAsync();
    }
    [Fact]
    public async Task Custom_receive_throw_after_seal_preserves_exact_fact_closes_response_and_denies_progression()
    {
        await using var s=await ZipScannerAuthorityTests.Setup.Create();ZipScannerInvocation? invocation=null;
        s.Provider.TransformSubmission=outcome=>{invocation=outcome.Response!.Invocation;return Task.FromResult(new ZipScannerSubmission(null,new ThrowAfterSeal(outcome.Response)));};
        await Assert.ThrowsAsync<IOException>(()=>s.Scan());var p=(await s.Read())!;Assert.True(s.Provider.ResponseClosed);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(invocation!.SealedObservation!.Evidence),p.Entries[0].EvidenceJson);
        Assert.Equal(ZipEntryState.Materialized,p.Entries[0].State);Assert.Equal(1,s.Provider.Calls["Payload"]);ZipScannerAuthorityTests.NoDownstream(s.Fixture,p);
        Assert.All(s.Provider.LastBuffer!,b=>Assert.Equal(0,b));Assert.All(s.Fixture.Runtime.ChildCrypto.LastPlaintext!,b=>Assert.Equal(0,b));
    }

}
