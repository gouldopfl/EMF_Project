using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class EvidencePackagePreparationService :
    IEvidencePackagePreparationService
{
    private readonly IEvidenceClassificationRepository _classifications;
    private readonly IEvidencePackageService _packages;

    public EvidencePackagePreparationService(
        IEvidenceClassificationRepository classifications,
        IEvidencePackageService packages)
    {
        ArgumentNullException.ThrowIfNull(classifications);
        ArgumentNullException.ThrowIfNull(packages);

        _classifications = classifications;
        _packages = packages;
    }

    public Task<EMF.Extensions.VeteransClaims.Models.Adjudication.EvidencePackage>
        PrepareAsync(
            EMF.Extensions.VeteransClaims.Models.Identities.ClaimIssueId claimIssueId,
            string purpose,
            string reviewerRole,
            CancellationToken cancellationToken = default) =>
        PrepareAsync(
            claimIssueId,
            purpose,
            reviewerRole,
            [],
            cancellationToken);

    public async Task<EMF.Extensions.VeteransClaims.Models.Adjudication.EvidencePackage>
        PrepareAsync(
            EMF.Extensions.VeteransClaims.Models.Identities.ClaimIssueId claimIssueId,
            string purpose,
            string reviewerRole,
            IReadOnlyCollection<EMF.Core.Models.Identities.ArtifactId>
                generatedOrganizationalMaterialArtifactIds,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            generatedOrganizationalMaterialArtifactIds);

        var classifications =
            await _classifications.GetEvidenceClassificationsAsync(
                claimIssueId,
                cancellationToken);

        var mismatchedClassification =
            classifications.FirstOrDefault(
                x => x.ClaimIssueId != claimIssueId);

        if (mismatchedClassification is not null)
        {
            throw new InvalidOperationException(
                $"Claim issue '{claimIssueId.Value}' classification lookup " +
                $"returned classification " +
                $"'{mismatchedClassification.Id.Value}' for claim issue " +
                $"'{mismatchedClassification.ClaimIssueId?.Value ?? "<none>"}'.");
        }

        var underlyingEvidenceArtifactIds =
            classifications
                .Select(x => x.ArtifactId)
                .Distinct()
                .ToArray();

        return await PrepareAsync(
            claimIssueId,
            purpose,
            reviewerRole,
            underlyingEvidenceArtifactIds,
            generatedOrganizationalMaterialArtifactIds,
            cancellationToken);
    }

    public async Task<EvidencePackage> PrepareAsync(
        ClaimIssueId claimIssueId,
        string purpose,
        string reviewerRole,
        IReadOnlyCollection<ArtifactId>
            underlyingEvidenceArtifactIds,
        IReadOnlyCollection<ArtifactId>
            generatedOrganizationalMaterialArtifactIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            underlyingEvidenceArtifactIds);

        ArgumentNullException.ThrowIfNull(
            generatedOrganizationalMaterialArtifactIds);

        var package =
            await _packages.CreateAsync(
                claimIssueId,
                purpose,
                reviewerRole,
                underlyingEvidenceArtifactIds.Distinct().ToArray(),
                generatedOrganizationalMaterialArtifactIds
                    .Distinct()
                    .ToArray(),
                cancellationToken);

        ValidatePackage(
            package,
            claimIssueId,
            purpose,
            reviewerRole);

        return package;
    }

    private static void ValidatePackage(
        EvidencePackage package,
        ClaimIssueId claimIssueId,
        string purpose,
        string reviewerRole)
    {
        ArgumentNullException.ThrowIfNull(package);

        if (package.ClaimIssueId != claimIssueId)
        {
            throw new InvalidOperationException(
                "Prepared evidence package belongs to another claim issue.");
        }

        if (!string.Equals(
                package.Purpose,
                purpose,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Prepared evidence package purpose mismatch.");
        }

        if (!string.Equals(
                package.ReviewerRole,
                reviewerRole,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Prepared evidence package reviewer role mismatch.");
        }
    }
}
