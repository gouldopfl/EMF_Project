using EMF.Core.Contracts.Zip;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

// Future authenticated hosts use this boundary; stock composition never supplies
// a grant. Recorded approval is insufficient, even when its expiry is in future.
public sealed class ZipAuthorizedOwnershipService(IZipParentAdmissionJournal admissions,
    IZipExtractionJournal parents, ZipDurableProfile profile, TimeProvider? timeProvider = null)
{
    public async Task<ZipParentSnapshot> ClaimAsync(IZipAuthorizedExecution grant, string owner, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(grant); ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, grant.Revoked);
        ct = linked.Token; ct.ThrowIfCancellationRequested();
        profile.ValidateRecoveryBinding(grant.Admission.Binding.ProfileJson, grant.Admission.Binding.ProfileHash);
        await grant.RevalidateAsync(ct);
        if (grant.ValidUntilUtc <= (timeProvider ?? TimeProvider.System).GetUtcNow())
            throw new UnauthorizedAccessException("ZIP execution grant expired.");
        using var deadline = new CancellationTokenSource(grant.ValidUntilUtc - (timeProvider ?? TimeProvider.System).GetUtcNow(), timeProvider ?? TimeProvider.System);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        ct = bounded.Token;
        var current = await admissions.ReadAdmissionAsync(grant.Admission.OperationId, ct)
            ?? throw new InvalidDataException("Missing original ZIP admission.");
        if (current != grant.Admission || current.State != ZipAdmissionState.ParentBound ||
            current.LatestAuthorityEvidence is null ||
            (current.LatestAuthorityEvidence.Capabilities & ZipAuthorityCapabilities.Recover) == 0)
            throw new UnauthorizedAccessException("ZIP recovery authority or admission changed.");
        ct.ThrowIfCancellationRequested();
        // Journal-side checks atomically veto recorded revocation; this live host
        // grant, rather than ClaimAsync's owner/epoch, establishes permission.
        var claimed = await parents.ClaimAsync(current.OperationId, owner, profile.OwnershipLease, ct);
        // A concurrency claim may wait on storage. Never return executable work
        // on a grant that expired or was revoked during that wait.
        ct.ThrowIfCancellationRequested();
        await grant.RevalidateAsync(ct);
        ct.ThrowIfCancellationRequested();
        if (grant.ValidUntilUtc <= (timeProvider ?? TimeProvider.System).GetUtcNow())
            throw new UnauthorizedAccessException("ZIP execution grant expired.");
        return claimed;
    }
}
