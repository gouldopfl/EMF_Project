using EMF.Core.Models.Identities;
using EMF.Core.Models;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.Tests.TestInfrastructure;

internal static class ReviewerPackageTestScope
{
    public static VeteransReviewerPackageDetails Create(EvidencePackage package, params string[] sources) => new()
    {
        PackageDetails = new EvidencePackageDetails
        {
            Package = package,
            Artifacts = sources.Select(id => new EvidencePackageArtifact
            {
                EvidencePackageId = package.Id, ArtifactId = new ArtifactId(id),
                ContentRole = EvidencePackageContentRoles.UnderlyingEvidence
            }).ToArray()
        },
        Artifacts = sources.Select(Artifact).ToArray(),
        ArtifactContents = sources.Select(id => new VeteransReviewerArtifactContent
        { Artifact = Artifact(id), Text = "Synthetic evidence" }).ToArray()
    };
    private static Artifact Artifact(string id) => new()
    { Id = new(id), Name = "Synthetic source.pdf", ArtifactType = "file" };
}
