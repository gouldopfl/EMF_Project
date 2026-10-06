using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Contracts;

// Trusted composition defers all live capture resources until OpenAsync. Merely
// constructing the factory must not open a read view or read source content.
public interface IReviewerCaptureSessionFactory
{
    Task<IReviewerCaptureSession> OpenAsync(OperationSnapshotId snapshotId,
        CancellationToken cancellationToken = default);
}
