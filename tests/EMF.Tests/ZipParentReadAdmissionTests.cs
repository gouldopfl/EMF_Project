using EMF.ConsoleApplication;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Integrity;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;
using EMF.Tests.TestInfrastructure;
namespace EMF.Tests;

public sealed class ZipParentReadAdmissionTests
{
    [Fact]
    public void SeparatelyComposedConsumersShareProcessWideGate()
    {
        Assert.Same(new ZipRuntimeComposition().Admission, new ZipRuntimeComposition(new(1024)).Admission);
        Assert.Same(ZipRuntimeComposition.Default.Admission, ZipParentReadAdmission.ProcessWide);
    }
    [Fact]
    public async Task WaitingCancellationDoesNotConsumeOrLeakPermitAndDisposalIsIdempotent()
    {
        var gate = ZipParentReadAdmission.ProcessWide;
        var first = await gate.AcquireAsync();
        try
        {
            using var cts = new CancellationTokenSource();
            var second = gate.AcquireAsync(cts.Token);
            Assert.False(second.IsCompleted);
            cts.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        }
        finally { first.Dispose(); first.Dispose(); }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var next = await gate.AcquireAsync(timeout.Token);
    }
    [Theory]
    [InlineData("success")] [InlineData("exception")] [InlineData("cancellation")]
    public async Task TwoWorkflowInstancesSerializeParentLifetimesAndClearBeforeSecondRead(string outcome)
    {
        var events = new List<string>();
        var firstStore = new Store(events, "first");
        var secondStore = new Store(events, "second") { BeforeRead = () =>
        {
            Assert.True(firstStore.Lease!.Disposed);
            Assert.All(firstStore.Lease.Bytes, b => Assert.Equal(0, b));
        }};
        var processor = new Processor(outcome);
        var first = await Activity("first", firstStore, processor);
        var second = await Activity("second", secondStore, new Processor("immediate"));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var firstTask = first.ExecuteAsync(new WorkflowExecutionContext { WorkflowId = new("zip-bounded-test") }, cts.Token);
        await processor.Entered.Task.WaitAsync(cts.Token);
        var secondTask = second.ExecuteAsync(new WorkflowExecutionContext { WorkflowId = new("zip-bounded-test") }, cts.Token);
        // The second asynchronous call reaches the busy process gate without reading source bytes.
        Assert.Equal(0, secondStore.Reads);
        if (outcome == "cancellation")
        {
            // Cancel processing and the waiting caller; retry proves neither leaks the permit.
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstTask);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => secondTask);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var retry = await second.ExecuteAsync(new WorkflowExecutionContext { WorkflowId = new("zip-bounded-test") }, timeout.Token);
            Assert.True(retry.Succeeded);
        }
        else
        {
            processor.Release.TrySetResult();
            var result = await firstTask;
            Assert.Equal(outcome == "success", result.Succeeded);
            Assert.True((await secondTask).Succeeded);
        }
        Assert.Equal(1, secondStore.Reads);
        Assert.True(events.IndexOf("first-stream-disposed") < events.IndexOf("first-lease-disposed"));
        Assert.True(events.IndexOf("first-lease-disposed") < events.IndexOf("second-read"));
    }
    private static async Task<ZipArchiveWorkflowActivity> Activity(string id, Store store, Processor processor)
    {
        var repository = new InMemoryEvidenceRepository();
        await repository.AddArtifactAsync(new Artifact { Id = new(id), Name = id + ".zip", ArtifactType = "file",
            Metadata = new Dictionary<string, object> { [ArtifactMetadataKeys.FileExtension] = ".zip" } });
        return new(repository, store, processor, new ContainerProcessingGuard(repository, new Sha256ContentFingerprintService()));
    }
    private sealed class Processor(string outcome) : IZipArchiveProcessingService
    {
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<ZipEntryExtractionResult>> ProcessAsync(ArtifactId id, ReadOnlyMemory<byte> bytes, CancellationToken ct = default) =>
            throw new Exception("Copying fallback forbidden");
        public async Task<IReadOnlyList<ZipEntryExtractionResult>> ProcessAsync(ArtifactId id, Stream stream, CancellationToken ct = default)
        {
            Assert.True(stream.CanRead); Assert.True(stream.CanSeek); Assert.Equal(3, stream.Length);
            Assert.Equal(1, stream.ReadByte());
            Entered.TrySetResult();
            if (outcome != "immediate") await Release.Task.WaitAsync(ct);
            if (outcome == "exception") throw new IOException("Injected processing failure");
            return Array.Empty<ZipEntryExtractionResult>();
        }
    }
    private sealed class Store(List<string> events, string name) : IArtifactContentStore, IBoundedVersionedArtifactContentStore
    {
        public int Reads; public Lease? Lease; public Action? BeforeRead;
        public long MaximumStoredRepresentationBytes => 150L * 1024 * 1024;
        public Task<IArtifactContentReadLease?> ReadBoundedVersionedAsync(ArtifactId id, BoundedArtifactContentReadRequest request, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested(); BeforeRead?.Invoke(); Reads++; events.Add(name + "-read");
            Lease = new(id, events, name);
            return Task.FromResult<IArtifactContentReadLease?>(Lease);
        }
        public Task<byte[]?> ReadAsync(ArtifactId id, CancellationToken ct = default) => throw new Exception("Ordinary fallback forbidden");
        public Task WriteAsync(ArtifactId id, ReadOnlyMemory<byte> b, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(ArtifactId id, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class Lease : IArtifactContentReadLease
    {
        private readonly ArtifactContentReadLease _inner; private readonly List<string> _events; private readonly string _name;
        public byte[] Bytes = [1, 2, 3]; public bool Disposed; private bool _streamDisposed;
        public Lease(ArtifactId id, List<string> events, string name)
        { _inner = new(id, new("revision"), 100, Bytes); _events = events; _name = name; }
        public ArtifactId ArtifactId => _inner.ArtifactId;
        public ArtifactContentRevision Revision => _inner.Revision;
        public long StoredLength => _inner.StoredLength;
        public long ReturnedLength => _inner.ReturnedLength;
        public ReadOnlyMemory<byte> Content => _inner.Content;
        public Stream OpenReadStream() => new RecordingStream(Bytes, () => { _streamDisposed = true; _events.Add(_name + "-stream-disposed"); });
        public void Dispose()
        {
            Assert.True(_streamDisposed); _inner.Dispose(); Disposed = true; _events.Add(_name + "-lease-disposed");
        }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
    private sealed class RecordingStream(byte[] bytes, Action disposed) : MemoryStream(bytes, writable: false)
    {
        private bool _disposed;
        protected override void Dispose(bool disposing)
        { if (!_disposed) { _disposed = true; disposed(); } base.Dispose(disposing); }
    }
}
