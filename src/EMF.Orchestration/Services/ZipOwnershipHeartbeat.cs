using EMF.Core.Contracts.Zip;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

// Optional host infrastructure; never registered by stock ZIP composition.
// Renewal is concurrency control and cannot issue or refresh authority grants.
public sealed class ZipOwnershipHeartbeat(IZipOwnershipJournal journal, TimeProvider? timeProvider = null)
{
    public async Task<T> RunAsync<T>(ZipFence ownership, Func<CancellationToken, Task<T>> execute,
        CancellationToken ct = default, TimeSpan? lease = null, TimeSpan? interval = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        var duration = lease ?? ZipDurableProfile.DefaultOwnershipLease;
        var cadence = interval ?? ZipDurableProfile.DefaultRenewalInterval;
        if (duration <= ZipNumericLimits.AttemptTimeout || cadence <= TimeSpan.Zero || cadence >= duration - ZipNumericLimits.AttemptTimeout)
            throw new ArgumentOutOfRangeException(nameof(interval));
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await journal.RenewOwnershipAsync(ownership.OperationId, ownership.Owner, ownership.Epoch, duration, ct);
        Exception? failure = null;
        async Task RenewAsync()
        {
            try
            {
                while (true)
                {
                    await Task.Delay(cadence, timeProvider ?? TimeProvider.System, stop.Token);
                    await journal.RenewOwnershipAsync(ownership.OperationId, ownership.Owner, ownership.Epoch, duration, stop.Token);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (ZipOwnershipReleasedException) { }
            catch (Exception error) { failure = error; await execution.CancelAsync(); }
        }
        var heartbeat = RenewAsync();
        try
        {
            T result;
            try { result = await execute(execution.Token); }
            catch (OperationCanceledException) when (failure is not null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw(); throw; }
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            return result;
        }
        finally
        {
            await stop.CancelAsync(); await heartbeat;
            // Observe a renewal failure that raced the successful work return.
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
