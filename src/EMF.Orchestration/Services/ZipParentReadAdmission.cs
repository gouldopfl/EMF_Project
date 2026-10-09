namespace EMF.Orchestration.Services;

/// <summary>One allocation-intensive ZIP parent lifetime per process, shared by all consumers.</summary>
public sealed class ZipParentReadAdmission
{
    public static ZipParentReadAdmission ProcessWide { get; } = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ZipParentReadAdmission() { }
    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        return new Permit(_gate);
    }
    private sealed class Permit(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
