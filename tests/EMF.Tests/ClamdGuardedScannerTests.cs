using System.Security.Cryptography;
using System.Text;
using EMF.Core.Contracts.Malware;
using EMF.Core.Contracts.Zip;
using EMF.Security.Malware;

namespace EMF.Tests;

[Collection(ZipProcessWideGateCollection.Name)]
public sealed class ClamdGuardedScannerTests
{
    internal sealed class ManualClock(DateTimeOffset now):TimeProvider
    {
        private readonly List<Timer> timers=[];public DateTimeOffset Now=now;internal Action? OnRead {get;set;}
        public override DateTimeOffset GetUtcNow(){OnRead?.Invoke();return Now;}
        internal int ActiveTimers=>timers.Count(t=>t.Active);
        internal void Advance(TimeSpan amount){Now+=amount;foreach(var timer in timers.ToArray())timer.Fire(Now);}
        public override ITimer CreateTimer(TimerCallback callback,object? state,TimeSpan dueTime,TimeSpan period)
        {var timer=new Timer(callback,state,Now+dueTime);timers.Add(timer);return timer;}
        private sealed class Timer(TimerCallback callback,object? state,DateTimeOffset due):ITimer
        {private bool disposed;public bool Active=>!disposed;public void Fire(DateTimeOffset now){if(!disposed&&now>=due){disposed=true;callback(state);}}
            public bool Change(TimeSpan dueTime,TimeSpan period)=>throw new NotSupportedException();public void Dispose()=>disposed=true;public ValueTask DisposeAsync(){Dispose();return ValueTask.CompletedTask;}}
    }
    private sealed class Guard:IZipScannerCallAdmissionGuard
    {
        private readonly CancellationTokenSource stop=new();internal Action<ZipScannerCall>? Before;
        internal Action<ZipScannerCall>? AfterInitiation {get;set;}
        public CancellationToken Invalidated=>stop.Token;
        internal void Revoke()=>stop.Cancel();
        public ValueTask RevalidateAsync(ZipScannerCall call,CancellationToken ct=default)
        {Before?.Invoke(call);ct.ThrowIfCancellationRequested();Invalidated.ThrowIfCancellationRequested();return ValueTask.CompletedTask;}
        public IDisposable AcceptCall(ZipScannerCall call,CancellationToken ct=default)
        {ct.ThrowIfCancellationRequested();Invalidated.ThrowIfCancellationRequested();return new Accepted(()=>AfterInitiation?.Invoke(call));}
        private sealed class Accepted(Action completed):IDisposable{public void Dispose()=>completed();}
    }
    internal sealed class Connection(string reply):IZipGuardedClamdConnection
    {
        private readonly byte[] bytes=Encoding.ASCII.GetBytes(reply);private int offset;
        internal readonly List<string> Actual=[];internal Func<string,Task>? After;internal Func<Task>? BeforeReceive;
        internal bool Disposed,Aborted;internal bool IgnoreReceiveCancellation {get;set;}internal Func<Task>? BeforeDispose {get;set;}
        public async Task WriteGuardedAsync(ReadOnlyMemory<byte> bytes,ZipScannerCall call,IZipScannerCallAdmissionGuard guard,CancellationToken ct)
        {await guard.RevalidateAsync(call,ct);using(var ticket=guard.AcceptCall(call,ct))Actual.Add(call.Kind);if(After is not null)await After(call.Kind);}
        public async Task FlushGuardedAsync(ZipScannerCall call,IZipScannerCallAdmissionGuard guard,CancellationToken ct)
        {await guard.RevalidateAsync(call,ct);using(var ticket=guard.AcceptCall(call,ct))Actual.Add(call.Kind);if(After is not null)await After(call.Kind);}
        public async Task<int> ReadGuardedAsync(Memory<byte> memory,ZipScannerCall call,IZipScannerCallAdmissionGuard guard,CancellationToken ct)
        {await guard.RevalidateAsync(call,ct);int n;using(var ticket=guard.AcceptCall(call,ct)){Actual.Add(call.Kind);n=Read(memory);}return n;}
        private int Read(Memory<byte> memory){if(offset>=bytes.Length)return 0;memory.Span[0]=bytes[offset++];return 1;}
        public async ValueTask<int> ReceiveAsync(Memory<byte> memory,CancellationToken ct)
        {Actual.Add("Receive");if(BeforeReceive is not null)await BeforeReceive();if(!IgnoreReceiveCancellation){ct.ThrowIfCancellationRequested();if(Aborted)throw new IOException("Aborted fixture transport.");}return Read(memory);}
        public void Abort()=>Aborted=true;
        public async ValueTask DisposeAsync(){if(!Disposed){if(BeforeDispose is not null)await BeforeDispose();Disposed=true;}}
    }
    internal sealed class Factory(Connection version,Connection scan):IZipGuardedClamdConnectionFactory
    {
        internal int Calls;
        public Task<Stream> ConnectAsync(CancellationToken ct=default)=>throw new InvalidOperationException("Raw connect bypass.");
        public async Task<IZipGuardedClamdConnection> ConnectGuardedAsync(ZipScannerCall call,IZipScannerCallAdmissionGuard guard,Action admitted,CancellationToken ct)
        {await guard.RevalidateAsync(call,ct);using(var ticket=guard.AcceptCall(call,ct)){admitted();Calls++;return Calls==1?version:scan;}}
    }
    private static ZipScannerInvocation Invocation(ClamdMalwareScanner scanner,byte[] bytes,TimeProvider clock)
        =>scanner.CreateInvocation(new("reservation",0,new string('A',64),new(new("private","revision",Convert.ToHexString(SHA256.HashData(bytes)),bytes.Length),scanner.Policy)),clock.GetUtcNow(),clock);
    [Theory]
    [InlineData("VersionWrite")][InlineData("ScanConnect")][InlineData("Header")]
    [InlineData("Payload")][InlineData("Terminator")][InlineData("FinalFlush")]
    public async Task Revoke_before_actual_transport_call_denies_it_without_reusing_prior_admission(string denied)
    {
        var version=new Connection("ClamAV 1.4.3/27897/date\0");var scan=new Connection("stream: OK\0");var guard=new Guard();
        guard.Before=call=>{if(call.Kind==denied)guard.Revoke();};var factory=new Factory(version,scan);var provider=new ClamdMalwareScanner(factory,ClamdMalwareScannerTests.Profile());
        var bytes="payload"u8.ToArray();using var source=new MemoryStream(bytes);var invocation=Invocation(provider,bytes,TimeProvider.System);
        var result=await provider.SubmitGuardedAsync(invocation,source,guard);Assert.Null(result.Response);Assert.NotNull(result.Observation);
        Assert.DoesNotContain(denied,scan.Actual);Assert.False(result.Observation!.Evidence.IsClean);Assert.Equal(MalwareScanFailure.Cancelled,result.Observation.Evidence.Failure);
        if(denied=="Payload")Assert.Contains("Header",scan.Actual);if(factory.Calls==2)Assert.True(scan.Disposed);else Assert.True(version.Disposed);
    }
    [Fact]
    public async Task Accepted_final_flush_can_finish_after_revocation_then_only_receive_existing_response()
    {
        var version=new Connection("ClamAV 1.4.3/27897/date\0");var scan=new Connection("stream: OK\0");var guard=new Guard();
        scan.After=kind=>{if(kind=="FinalFlush")guard.Revoke();return Task.CompletedTask;};var provider=new ClamdMalwareScanner(new Factory(version,scan),ClamdMalwareScannerTests.Profile());
        byte[]? buffer=null;provider.BufferAllocated=b=>buffer=b;var bytes="payload"u8.ToArray();var source=new MemoryStream(bytes);var invocation=Invocation(provider,bytes,TimeProvider.System);
        var submitted=await provider.SubmitGuardedAsync(invocation,source,guard);Assert.NotNull(submitted.Response);source.Dispose();Assert.All(buffer!,b=>Assert.Equal(0,b));
        var before=scan.Actual.Where(n=>n!="Receive").ToArray();await using var response=submitted.Response!;
        var observed=await response.ReceiveObservationAsync();Assert.Equal(MalwareCoverage.Unknown,observed.Evidence.Coverage);Assert.Equal(MalwareScanFailure.CoverageUnproven,observed.Evidence.Failure);
        Assert.False(observed.Evidence.IsClean);Assert.Equal(before,scan.Actual.Where(n=>n!="Receive").ToArray());Assert.True(invocation.Owns(observed));
    }
    [Fact]
    public async Task Revocation_mid_reply_receipt_never_enables_another_source_or_transport_write()
    {
        var version=new Connection("ClamAV 1.4.3/27897/date\0");var scan=new Connection("stream: OK\0");var guard=new Guard();
        var provider=new ClamdMalwareScanner(new Factory(version,scan),ClamdMalwareScannerTests.Profile());var bytes="payload"u8.ToArray();using var source=new MemoryStream(bytes);
        var submitted=await provider.SubmitGuardedAsync(Invocation(provider,bytes,TimeProvider.System),source,guard);
        var reads=0;scan.BeforeReceive=()=>{if(++reads==3)guard.Revoke();return Task.CompletedTask;};
        await using var response=submitted.Response!;var observation=await response.ReceiveObservationAsync();Assert.False(observation.Evidence.IsClean);
        Assert.Equal(1,scan.Actual.Count(n=>n=="Payload"));Assert.Equal(1,scan.Actual.Count(n=>n=="FinalFlush"));Assert.Equal(MalwareScanFailure.CoverageUnproven,observation.Evidence.Failure);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Original_deadline_or_hard_abort_during_reply_denies_timely_success_and_aborts_connection(bool hardAbort)
    {
        var clock=new ManualClock(DateTimeOffset.UtcNow);var version=new Connection("ClamAV 1.4.3/27897/date\0");var scan=new Connection("stream: OK\0");var guard=new Guard();
        var provider=new ClamdMalwareScanner(new Factory(version,scan),ClamdMalwareScannerTests.Profile());byte[]? buffer=null;provider.BufferAllocated=b=>buffer=b;
        var bytes="payload"u8.ToArray();using var source=new MemoryStream(bytes);
        var invocation=Invocation(provider,bytes,clock);var submitted=await provider.SubmitGuardedAsync(invocation,source,guard);source.Dispose();using var abort=new CancellationTokenSource();
        var admittedSends=scan.Actual.Where(n=>n!="Receive").ToArray();
        var arrived=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scan.BeforeReceive=()=>{arrived.TrySetResult();return release.Task;};await using var response=submitted.Response!;
        var receive=response.ReceiveObservationAsync(abort.Token);await arrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if(hardAbort)abort.Cancel();else clock.Advance(ZipNumericLimits.AttemptTimeout);
        Assert.True(scan.Aborted);release.SetResult();var observed=await receive;
        Assert.Equal(hardAbort?MalwareScanFailure.Cancelled:MalwareScanFailure.TimedOut,observed.Evidence.Failure);Assert.False(observed.Evidence.IsClean);
        Assert.Equal(ZipScanObservationOrigin.AdmittedLocalFailure,observed.Origin);Assert.Single(scan.Actual.Where(n=>n=="Receive"));
        Assert.Equal(admittedSends,scan.Actual.Where(n=>n!="Receive").ToArray());Assert.All(buffer!,b=>Assert.Equal(0,b));
        await response.DisposeAsync();Assert.True(scan.Disposed);Assert.True(scan.Aborted);
    }
    [Theory]
    [InlineData("stream: OK\0",MalwareScanFailure.CoverageUnproven)]
    [InlineData("stream: WEIRD\0",MalwareScanFailure.ProtocolError)]
    public async Task Guarded_clamd_interpretation_matches_legacy_without_promoting_coverage(string reply,MalwareScanFailure failure)
    {
        var version=new Connection("ClamAV 1.4.3/27897/date\0");var scan=new Connection(reply);var provider=new ClamdMalwareScanner(new Factory(version,scan),ClamdMalwareScannerTests.Profile());
        var bytes="payload"u8.ToArray();using var source=new MemoryStream(bytes);var invocation=Invocation(provider,bytes,TimeProvider.System);
        var submitted=await provider.SubmitGuardedAsync(invocation,source,new Guard());await using var response=submitted.Response!;
        var observed=await response.ReceiveObservationAsync();Assert.Equal(failure,observed.Evidence.Failure);Assert.False(observed.Evidence.IsClean);MalwareScanValidation.Evidence(observed.Evidence);
        Assert.Equal(failure==MalwareScanFailure.ProtocolError?ZipScanObservationOrigin.AdmittedLocalFailure:ZipScanObservationOrigin.ProviderReturned,observed.Origin);
    }
    [Fact]
    public async Task Opaque_raw_transport_is_rejected_before_actual_connection_or_read()
    {
        var factory=ClamdMalwareScannerTests.Replies("stream: OK\0");var provider=new ClamdMalwareScanner(factory,ClamdMalwareScannerTests.Profile());var bytes="payload"u8.ToArray();
        using var source=new MemoryStream(bytes);await Assert.ThrowsAsync<NotSupportedException>(()=>provider.SubmitGuardedAsync(Invocation(provider,bytes,TimeProvider.System),source,new Guard()));Assert.Equal(0,factory.Calls);
    }
    [Fact]
    public async Task Response_disposal_and_oversized_reply_cannot_create_an_executable_or_reusable_handle()
    {
        var version=new Connection("ClamAV 1.4.3/27897/date\0");var scan=new Connection(new string('X',1025)+"\0");var provider=new ClamdMalwareScanner(new Factory(version,scan),ClamdMalwareScannerTests.Profile());
        var bytes="payload"u8.ToArray();using var source=new MemoryStream(bytes);var submitted=await provider.SubmitGuardedAsync(Invocation(provider,bytes,TimeProvider.System),source,new Guard());
        var response=submitted.Response!;var observed=await response.ReceiveObservationAsync();Assert.Equal(MalwareScanFailure.ProtocolError,observed.Evidence.Failure);
        Assert.Equal(1024,scan.Actual.Count(n=>n=="Receive"));await response.DisposeAsync();Assert.True(scan.Disposed);Assert.True(scan.Aborted);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>response.ReceiveObservationAsync());
    }
    [Fact]
    public async Task Disposal_failure_still_aborts_actual_connection_and_keeps_sealed_observation_unchanged()
    {
        var version=new Connection("ClamAV 1.4.3/27897/date\0");var scan=new Connection("stream: OK\0");var guard=new Guard();
        var provider=new ClamdMalwareScanner(new Factory(version,scan),ClamdMalwareScannerTests.Profile());byte[]? buffer=null;provider.BufferAllocated=b=>buffer=b;
        var bytes="payload"u8.ToArray();using var source=new MemoryStream(bytes);var submitted=await provider.SubmitGuardedAsync(Invocation(provider,bytes,TimeProvider.System),source,guard);
        var response=submitted.Response!;var sealedObservation=await response.ReceiveObservationAsync();var frozen=System.Text.Json.JsonSerializer.Serialize(sealedObservation.Evidence);
        var calls=scan.Actual.ToArray();scan.BeforeDispose=()=>{guard.Revoke();throw new IOException("Failure after abort/close.");};
        await Assert.ThrowsAsync<IOException>(()=>response.DisposeAsync().AsTask());Assert.True(scan.Aborted);
        Assert.Equal(calls,scan.Actual);Assert.Equal(frozen,System.Text.Json.JsonSerializer.Serialize(sealedObservation.Evidence));Assert.All(buffer!,b=>Assert.Equal(0,b));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>response.ReceiveObservationAsync());
    }
    [Fact]
    public async Task Real_TCP_guarded_transport_closes_socket_after_revoked_receive_only_completion_without_extra_send()
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);listener.Start();
        var port=((System.Net.IPEndPoint)listener.LocalEndpoint).Port;var bytes="guarded TCP payload"u8.ToArray();
        async Task<string> Command(Stream stream)
        {var text=new StringBuilder();var b=new byte[1];while(true){await stream.ReadExactlyAsync(b,timeout.Token);if(b[0]==0)return text.ToString();text.Append((char)b[0]);}}
        var server=Task.Run(async()=>
        {
            using(var client=await listener.AcceptTcpClientAsync(timeout.Token))
            {using var stream=client.GetStream();Assert.Equal("zVERSION",await Command(stream));await stream.WriteAsync("ClamAV 1.4.3/27897/date\0"u8.ToArray(),timeout.Token);}
            using(var client=await listener.AcceptTcpClientAsync(timeout.Token))
            {
                using var stream=client.GetStream();Assert.Equal("zINSTREAM",await Command(stream));var header=new byte[4];var payload=new byte[65536];var total=0;
                while(true){await stream.ReadExactlyAsync(header,timeout.Token);var n=(int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header);if(n==0)break;
                    await stream.ReadExactlyAsync(payload.AsMemory(0,n),timeout.Token);Assert.True(payload.AsSpan(0,n).SequenceEqual(bytes.AsSpan(total,n)));total+=n;}
                Assert.Equal(bytes.Length,total);await stream.WriteAsync("stream: OK\0"u8.ToArray(),timeout.Token);
                Assert.Equal(0,await stream.ReadAsync(header.AsMemory(0,1),timeout.Token));
            }
        });
        try
        {
            var guard=new Guard();guard.AfterInitiation=call=>{if(call.Kind=="FinalFlush")guard.Revoke();};
            var provider=new ClamdMalwareScanner(new ClamdTcpConnectionFactory("127.0.0.1",port),ClamdMalwareScannerTests.Profile());byte[]? buffer=null;provider.BufferAllocated=b=>buffer=b;
            var source=new MemoryStream(bytes);var submitted=await provider.SubmitGuardedAsync(Invocation(provider,bytes,TimeProvider.System),source,guard,timeout.Token);
            source.Dispose();

            var response=submitted.Response;
            Assert.True(response is not null,
                $"Expected receive-only response after successful submission; " +
                $"Origin={submitted.Observation?.Origin.ToString()??"<none>"}, " +
                $"Failure={submitted.Observation?.Evidence.Failure.ToString()??"<none>"}, " +
                $"Detection={submitted.Observation?.Evidence.Detection.ToString()??"<none>"}, " +
                $"Coverage={submitted.Observation?.Evidence.Coverage.ToString()??"<none>"}.");

            {
                await using var receiveOnly=response!;
                var observed=await receiveOnly.ReceiveObservationAsync(timeout.Token);
                Assert.Equal(MalwareScanFailure.CoverageUnproven,observed.Evidence.Failure);
                Assert.False(observed.Evidence.IsClean);
            }

            await server;
            Assert.All(buffer!,b=>Assert.Equal(0,b));
        }
        finally
        {
            timeout.Cancel();
            listener.Stop();
            try { await server; }
            catch
            {
                // Success path already observes server failures;
                // cleanup must not hide the original test failure.
            }
        }
    }

    [Fact]
    public async Task Noncooperative_late_receive_cannot_seal_a_provider_result_beyond_original_deadline()
    {
        var clock=new ManualClock(DateTimeOffset.UtcNow);var version=new Connection("ClamAV 1.4.3/27897/date\0");var scan=new Connection("stream: OK\0"){IgnoreReceiveCancellation=true};
        var provider=new ClamdMalwareScanner(new Factory(version,scan),ClamdMalwareScannerTests.Profile());var bytes="payload"u8.ToArray();using var source=new MemoryStream(bytes);
        var invocation=Invocation(provider,bytes,clock);var submitted=await provider.SubmitGuardedAsync(invocation,source,new Guard());
        var arrived=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scan.BeforeReceive=()=>{arrived.TrySetResult();return release.Task;};await using var response=submitted.Response!;
        var receive=response.ReceiveObservationAsync();await arrived.Task.WaitAsync(TimeSpan.FromSeconds(10));clock.Advance(ZipNumericLimits.AttemptTimeout);release.SetResult();
        var observed=await receive;Assert.Equal(MalwareScanFailure.TimedOut,observed.Evidence.Failure);Assert.Equal(ZipScanObservationOrigin.AdmittedLocalFailure,observed.Origin);
        Assert.False(observed.Evidence.IsClean);Assert.Single(scan.Actual.Where(n=>n=="Receive"));Assert.True(scan.Aborted);
    }

    [Theory]
    [InlineData("abort")][InlineData("deadline")]
    public async Task Cancellation_or_deadline_at_exact_observation_seal_is_a_local_abort_not_provider_success(string action)
    {
        var clock=new ManualClock(DateTimeOffset.UtcNow);var version=new Connection("ClamAV 1.4.3/27897/date\0");var scan=new Connection("stream: OK\0");
        var provider=new ClamdMalwareScanner(new Factory(version,scan),ClamdMalwareScannerTests.Profile());var bytes="payload"u8.ToArray();using var source=new MemoryStream(bytes);
        var submitted=await provider.SubmitGuardedAsync(Invocation(provider,bytes,clock),source,new Guard());using var abort=new CancellationTokenSource();
        var received=0;var finalChecks=0;
        scan.BeforeReceive=()=>{if(++received==11)clock.OnRead=()=>{if(++finalChecks==4){if(action=="abort")abort.Cancel();else clock.Advance(ZipNumericLimits.AttemptTimeout);}};return Task.CompletedTask;};
        await using var response=submitted.Response!;var observed=await response.ReceiveObservationAsync(abort.Token);
        Assert.True(finalChecks>=4);Assert.Equal(ZipScanObservationOrigin.AdmittedLocalFailure,observed.Origin);
        Assert.Equal(action=="abort"?MalwareScanFailure.Cancelled:MalwareScanFailure.TimedOut,observed.Evidence.Failure);Assert.False(observed.Evidence.IsClean);Assert.True(scan.Aborted);
    }
    [Fact]
    public async Task Sealed_local_observation_survives_submission_connection_disposal_failure()
    {
        var version=new Connection("malformed version\0");var scan=new Connection("stream: OK\0");
        version.BeforeDispose=()=>throw new IOException("Deterministic version close failure.");
        var provider=new ClamdMalwareScanner(new Factory(version,scan),ClamdMalwareScannerTests.Profile());var bytes="payload"u8.ToArray();using var source=new MemoryStream(bytes);
        var invocation=Invocation(provider,bytes,TimeProvider.System);
        await Assert.ThrowsAsync<IOException>(()=>provider.SubmitGuardedAsync(invocation,source,new Guard()));
        Assert.True(version.Aborted);Assert.NotNull(invocation.SealedObservation);Assert.True(invocation.Owns(invocation.SealedObservation!));
        Assert.Equal(MalwareScanFailure.ProtocolError,invocation.SealedObservation!.Evidence.Failure);Assert.Equal(ZipScanObservationOrigin.AdmittedLocalFailure,invocation.SealedObservation.Origin);
        Assert.Empty(scan.Actual);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Guarded_clamd_text_and_nested_zip_OK_preserve_unknown_coverage(bool nested)
    {
        var bytes=nested?EMF.Tests.TestInfrastructure.ZipDurableRuntimeFixture.Zip():"text payload"u8.ToArray();
        var version=new Connection("ClamAV 1.4.3/27897/date\0");var scan=new Connection("stream: OK\0");
        var provider=new ClamdMalwareScanner(new Factory(version,scan),ClamdMalwareScannerTests.Profile());using var source=new MemoryStream(bytes);
        var submitted=await provider.SubmitGuardedAsync(Invocation(provider,bytes,TimeProvider.System),source,new Guard());await using var response=submitted.Response!;
        var observation=await response.ReceiveObservationAsync();Assert.Equal(MalwareDetection.NoThreatDetected,observation.Evidence.Detection);
        Assert.Equal(MalwareCoverage.Unknown,observation.Evidence.Coverage);Assert.Equal(MalwareScanFailure.CoverageUnproven,observation.Evidence.Failure);Assert.False(observation.Evidence.IsClean);
        Assert.True(ZipScanOutcome.IsTerminal(observation.Evidence,1));
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Reply_and_version_scratch_buffers_clear_on_success_or_receive_failure(bool fail)
    {
        var version=new Connection("ClamAV 1.4.3/27897/date\0");var scan=new Connection(fail?"malformed reply\n\0":"stream: OK\0");
        var provider=new ClamdMalwareScanner(new Factory(version,scan),ClamdMalwareScannerTests.Profile());var buffers=new List<byte[]>();
        provider.ScratchBufferAllocated=b=>buffers.Add(b);provider.BufferAllocated=b=>buffers.Add(b);
        var bytes="payload"u8.ToArray();using var source=new MemoryStream(bytes);
        var submitted=await provider.SubmitGuardedAsync(Invocation(provider,bytes,TimeProvider.System),source,new Guard());
        await using var response=submitted.Response!;await response.ReceiveObservationAsync();Assert.Equal(5,buffers.Count);
        Assert.All(buffers,b=>Assert.All(b,value=>Assert.Equal(0,value)));await response.DisposeAsync();Assert.True(scan.Aborted);Assert.True(scan.Disposed);
    }

}
