using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Contracts;

// Ownership primitives only. No repository adapter, Ready status, authorization,
// renderer-input acceptance or live capture capability is promised by this API.
public interface IReviewerAssemblyCapturedInputs : IDisposable
{
    string CanonicalSha256 { get; }
    IReviewerAssemblyInputCopyLease Acquire(CancellationToken cancellationToken = default);
}

public interface IReviewerAssemblyInputCopyLease : IDisposable
{
    // Fresh detached result on every call. Its controlled buffers belong to this
    // lease and remain valid until disposal. Caller-inserted buffers remain foreign.
    ReviewerAssemblyCaptureManifest ReadCopy(CancellationToken cancellationToken = default);
}
