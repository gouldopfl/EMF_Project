using EMF.Extensions.VeteransClaims.Contracts;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class EvidencePackagePreparationService
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

    public async Task<EMF.Extensions.VeteransClaims.Models.Adjudication.EvidencePackage>
        PrepareAsync(
            EMF.Extensions.VeteransClaims.Models.Identities.ClaimIssueId claimIssueId,
            string purpose,
            string reviewerRole,
            CancellationToken cancellationToken = default)
    {
        var classifications =
            await _classifications.GetEvidenceClassificationsAsync(
                claimIssueId,
                cancellationToken);

        var package =
            await _packages.CreateAsync(
                claimIssueId,
                purpose,
                reviewerRole,
                cancellationToken);

        foreach (var artifactId in
            classifications
                .Select(x => x.ArtifactId)
                .Distinct())
        {
            await _packages.AddArtifactAsync(
                package.Id,
                artifactId,
                EMF.Extensions.VeteransClaims.Models.Adjudication
                    .EvidencePackageContentRoles.UnderlyingEvidence,
                cancellationToken);
        }

        return package;
    }
}
