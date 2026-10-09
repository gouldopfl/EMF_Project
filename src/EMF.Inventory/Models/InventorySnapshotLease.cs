using EMF.Inventory.Storage;

namespace EMF.Inventory.Models;

public sealed class InventorySnapshotLease : IAsyncDisposable
{
    private enum ReleaseState { Active, Releasing, Released }
    private readonly LinuxInventorySnapshotWorkspace _workspace;
    private readonly object _release = new();
    private Task? _attempt;
    private readonly string? _ephemeralParent;
    private IAsyncDisposable? _execution;
    private ReleaseState _state;
    public string Path { get; }
    internal InventorySnapshotLease(LinuxInventorySnapshotWorkspace workspace, string path,
        string? ephemeralParent = null, IAsyncDisposable? execution = null)
        => (_workspace, Path, _ephemeralParent, _execution) = (workspace, path, ephemeralParent, execution);
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource completion;
        lock (_release)
        {
            if (_state == ReleaseState.Released) return ValueTask.CompletedTask;
            if (_attempt is not null) return new(_attempt);
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _attempt = completion.Task;
            _state = ReleaseState.Releasing;
        }
        _ = ReleaseAttemptAsync(completion);
        return new(completion.Task);
    }
    private async Task ReleaseAttemptAsync(TaskCompletionSource completion)
    {
        Exception? failure = null;
        try
        {
            if (_ephemeralParent is not null && _execution is null)
                _execution = await _workspace.AcquireAsync(_ephemeralParent).ConfigureAwait(false);
            await _workspace.RemoveAsync(Path).ConfigureAwait(false);
        }
        catch (Exception error) { failure = error; }
        finally
        {
            // Disposal ends input consumption. The durable owner marker and external
            // cleanup intent preserve recovery identity on failure. A retry
            // reacquires exclusion. A still-active input keeps its lifetime gate.
            if (_execution is not null)
            {
                try { await _execution.DisposeAsync().ConfigureAwait(false); }
                catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
                _execution = null;
            }
        }
        lock (_release)
        {
            _state = failure is null ? ReleaseState.Released : ReleaseState.Active;
            if (failure is null) completion.TrySetResult(); else completion.TrySetException(failure);
            _attempt = null;
        }
    }
}
