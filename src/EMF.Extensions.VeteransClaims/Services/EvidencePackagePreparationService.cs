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

        var underlyingEvidenceArtifactIds =
            classifications
                .Select(x => x.ArtifactId)
                .Distinct()
                .ToArray();

        return await _packages.CreateAsync(
            claimIssueId,
            purpose,
            reviewerRole,
            underlyingEvidenceArtifactIds,
            generatedOrganizationalMaterialArtifactIds
                .Distinct()
                .ToArray(),
            cancellationToken);
    }
}
