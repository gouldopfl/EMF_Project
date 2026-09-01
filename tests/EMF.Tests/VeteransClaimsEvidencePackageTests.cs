using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed partial class VeteransClaimsEvidencePackageTests
{
    [Fact]
    public void EvidencePackage_PreservesIssuePurposeAndReviewerRole()
    {
        var package = new EvidencePackage
        {
            Id = new EvidencePackageId("package-001"),
            ClaimIssueId = new ClaimIssueId("claim-issue-001"),
            Purpose = "Medical review",
            ReviewerRole = "MedicalProfessional"
        };

        Assert.Equal("Medical review", package.Purpose);
        Assert.Equal("MedicalProfessional", package.ReviewerRole);
    }

    [Fact]
    public void EvidencePackageArtifact_ReferencesPlatformArtifactAndRole()
    {
        var artifact = new EvidencePackageArtifact
        {
            EvidencePackageId = new EvidencePackageId("package-001"),
            ArtifactId = new ArtifactId("artifact-001"),
            ContentRole = EvidencePackageContentRoles.UnderlyingEvidence
        };

        Assert.Equal(new ArtifactId("artifact-001"), artifact.ArtifactId);
        Assert.Equal(
            EvidencePackageContentRoles.UnderlyingEvidence,
            artifact.ContentRole);
    }
}

public sealed partial class VeteransClaimsEvidencePackageTests
{
    [Fact]
    public void EvidencePackageDetails_ComposesPackageAndArtifacts()
    {
        var package =
            new EvidencePackage
            {
                Id = new EvidencePackageId("package-001"),
                ClaimIssueId = new ClaimIssueId("claim-issue-001"),
                Purpose = "Medical review",
                ReviewerRole = "MedicalProfessional"
            };

        var artifact =
            new EvidencePackageArtifact
            {
                EvidencePackageId = package.Id,
                ArtifactId = new ArtifactId("artifact-001"),
                ContentRole =
                    EvidencePackageContentRoles.UnderlyingEvidence
            };

        var details =
            new EvidencePackageDetails
            {
                Package = package,
                Artifacts = [artifact]
            };

        Assert.Same(
            package,
            details.Package);

        Assert.Same(
            artifact,
            Assert.Single(details.Artifacts));
    }
}
