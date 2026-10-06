using System.Security.Cryptography;
using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Orchestration;

// Owns the independently decoded buffers, including buffers a consumer removes
// from the mutable Content dictionary before disposing the lease.
public sealed class ReviewerRetainedBundleLease : IDisposable, IAsyncDisposable
{
    private ReviewerCapturedBundle? bundle;
    private readonly byte[][] owned;

    internal ReviewerRetainedBundleLease(ReviewerCapturedBundle bundle)
    {
        this.bundle = bundle;
        owned = bundle.Content.Values.ToArray();
    }

    public ReviewerCapturedBundle Bundle => Volatile.Read(ref bundle)
        ?? throw new ObjectDisposedException(nameof(ReviewerRetainedBundleLease));

    public void Dispose()
    {
        var released = Interlocked.Exchange(ref bundle, null);
        if (released is null) return;
        foreach (var bytes in owned) CryptographicOperations.ZeroMemory(bytes);
        foreach (var bytes in released.Content.Values)
            if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
