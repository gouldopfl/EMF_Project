using EMF.Core.Models;
using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerPackageDetails
{
    public required EvidencePackageDetails PackageDetails
    { get; init; }

    public required IReadOnlyList<Artifact> Artifacts
    { get; init; }

    public IReadOnlyList<VeteransReviewerArtifactContent> ArtifactContents
    { get; init; } =
        Array.Empty<VeteransReviewerArtifactContent>();
}
