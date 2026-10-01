using EMF.Common;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Orchestration;

/// <summary>
/// Application boundary for the future Preserve / Refresh UI. Preparation is
/// explicit; print/export has no sourcing callback and cannot refresh dates.
/// See docs/REVIEWER_PACKAGE_DETERMINISM_CONTRACT.md.
/// </summary>
public sealed class VeteransReviewerPresentationVersionService(IEvidencePackageRepository packages,
    Func<EmfVerifiedFirstPartyDeploymentIdentity>? verifyDeployment = null)
{
    public async Task<ReviewerPackagePresentationSnapshot> PreserveAsync(EvidencePackageId packageId,
        CancellationToken cancellationToken = default) =>
        await packages.GetReviewerPresentationAsync(packageId, cancellationToken)
            ?? throw new InvalidDataException("Package has no frozen presentation; explicit preparation and validation are required.");

    /// <summary>
    /// Caller supplies a newly resolved snapshot with a fresh package identity and
    /// explicit dates. No regulatory fetch, AI, clock or live-data selection occurs
    /// here. Refresh any other visible source dates during the upstream preparation.
    /// </summary>
    public async Task<ReviewerPackagePresentationSnapshot> PrepareNewVersionAsync(
        EvidencePackageId previousPackageId, ReviewerPackageSnapshot newlyResolvedSource,
        VeteransReviewerPackageRenderSettings settings, ReviewerPackageCover cover,
        CancellationToken cancellationToken = default) =>
        await PrepareVersionAsync(previousPackageId, newlyResolvedSource, settings, cover, false, cancellationToken);

    /// <summary>
    /// Explicitly completes a previously interrupted version using caller-reviewed
    /// intended inputs. Existing membership and any sealed source must match exactly.
    /// Historical exported packages and already frozen presentations cannot be recovered.
    /// </summary>
    public async Task<ReviewerPackagePresentationSnapshot> RecoverIncompleteVersionAsync(
        EvidencePackageId previousPackageId, ReviewerPackageSnapshot intendedSource,
        VeteransReviewerPackageRenderSettings settings, ReviewerPackageCover cover,
        CancellationToken cancellationToken = default) =>
        await PrepareVersionAsync(previousPackageId, intendedSource, settings, cover, true, cancellationToken);

    private async Task<ReviewerPackagePresentationSnapshot> PrepareVersionAsync(
        EvidencePackageId previousPackageId, ReviewerPackageSnapshot newlyResolvedSource,
        VeteransReviewerPackageRenderSettings settings, ReviewerPackageCover cover,
        bool recoverIncomplete, CancellationToken cancellationToken)
    {
        if (!packages.SupportsReviewerPresentationSnapshots)
            throw new NotSupportedException("Frozen presentation persistence is required.");
        var previous = await PreserveAsync(previousPackageId, cancellationToken);
        var oldSource = await packages.GetReviewerSnapshotAsync(previousPackageId, cancellationToken)
            ?? throw new InvalidDataException("Previous package source is missing.");
        var oldDetails = VeteransReviewerPackageSnapshot.Restore(oldSource).Details;
        var current = VeteransReviewerPackageSnapshot.Restore(newlyResolvedSource).Details;
        if (previous.PackageId == newlyResolvedSource.PackageId ||
            oldDetails.PackageDetails.Package.ClaimIssueId != current.PackageDetails.Package.ClaimIssueId)
            throw new InvalidDataException("New package version requires a fresh identity in the same claim issue.");
        var existing = await packages.GetEvidencePackageAsync(newlyResolvedSource.PackageId, cancellationToken);
        if (recoverIncomplete && existing is null)
            throw new InvalidDataException("Incomplete package version identity does not exist.");
        if (!recoverIncomplete && existing is not null)
            throw new InvalidDataException("New package version identity already exists.");
        var deployment = verifyDeployment?.Invoke()
            ?? throw new InvalidOperationException("New package preparation requires verified deployment identity.");
        VeteransReviewerPackageRendererIdentity.ValidateVerifiedDeployment(deployment);
        var prepared = VeteransReviewerPackagePresentationPreparation.Prepare(newlyResolvedSource,
            settings, cover, previousPackageId.Value);
        if (recoverIncomplete)
            await packages.RecoverReviewerPresentationVersionAsync(newlyResolvedSource,
                current.PackageDetails, prepared, cancellationToken);
        else
            await packages.CreateReviewerPresentationVersionAsync(newlyResolvedSource,
                current.PackageDetails, prepared, cancellationToken);
        return prepared;
    }
}
