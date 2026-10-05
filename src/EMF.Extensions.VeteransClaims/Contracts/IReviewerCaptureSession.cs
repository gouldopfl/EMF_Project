using EMF.Extensions.VeteransClaims.Models.Adjudication;
namespace EMF.Extensions.VeteransClaims.Contracts;
// Single-use owner of a bounded live read view. CaptureAsync closes owned live
// resources on every exit; an unused session closes when its overall deadline fires.
// Callers should still use await using for prompt release when abandoning capture.
public interface IReviewerCaptureSession : IAsyncDisposable
{
    /// <summary>
    /// Captures the read view and releases owned live resources on every exit.
    /// Successful completion transfers ownership of the returned
    /// <see cref="ReviewerCapturedBundle.Content"/> byte[] buffers to the caller.
    /// Implementations must return independent capture-owned buffers, never
    /// borrowed or provider-owned source memory as those Content buffers.
    /// </summary>
    Task<ReviewerCapturedBundle> CaptureAsync(CancellationToken cancellationToken = default);
}
public interface IReviewerRetainedMaterialStore
{
    Task<ReviewerRetainedReference> RetainAsync(ReviewerCapturedBundle bundle, CancellationToken cancellationToken = default);
    Task<ReviewerCapturedBundle> ReadValidatedAsync(ReviewerRetainedReference reference, CancellationToken cancellationToken = default);
}
