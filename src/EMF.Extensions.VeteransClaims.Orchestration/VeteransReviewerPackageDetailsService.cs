using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerPackageDetailsService
{
    private readonly IEvidencePackageService _packages;
    private readonly IEvidenceRepository _evidence;

    public VeteransReviewerPackageDetailsService(
        IEvidencePackageService packages,
        IEvidenceRepository evidence)
    {
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(evidence);

        _packages = packages;
        _evidence = evidence;
    }

    public async Task<VeteransReviewerPackageDetails?> GetAsync(
        EvidencePackageId packageId,
        CancellationToken cancellationToken = default)
    {
        var details =
            await _packages.GetAsync(
                packageId,
                cancellationToken);

        if (details is null)
            return null;

        var artifacts = new List<Artifact>();

        foreach (var packageArtifact in details.Artifacts)
        {
            var artifact =
                await _evidence.GetArtifactAsync(
                    packageArtifact.ArtifactId,
                    cancellationToken);

            if (artifact is not null)
                artifacts.Add(artifact);
        }

        return new VeteransReviewerPackageDetails
        {
            PackageDetails = details,
            Artifacts = artifacts
        };
    }
}
