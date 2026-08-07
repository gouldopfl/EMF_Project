using EMF.Core.Models.Integrity;

namespace EMF.Core.Contracts;

public interface IContentFingerprintService
{
    Task<ContentFingerprint> ComputeAsync(
        string sourcePath,
        CancellationToken cancellationToken = default);
}
