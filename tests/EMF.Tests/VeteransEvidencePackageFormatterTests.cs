using EMF.ConsoleApplication;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransEvidencePackageFormatterTests
{
    [Fact]
    public void Format_IncludesPackageAndArtifactRoles()
    {
        var packageId = new EvidencePackageId("package-1");

        var details = new EvidencePackageDetails
        {
            Package = new EvidencePackage
            {
                Id = packageId,
                ClaimIssueId = new ClaimIssueId("issue-1"),
                Purpose = "Physician reviewer package",
                ReviewerRole = "MedicalProfessional"
            },
            Artifacts =
            [
                new EvidencePackageArtifact
                {
                    EvidencePackageId = packageId,
                    ArtifactId = new ArtifactId("source-1"),
                    ContentRole =
                        EvidencePackageContentRoles.UnderlyingEvidence
                },
                new EvidencePackageArtifact
                {
                    EvidencePackageId = packageId,
                    ArtifactId = new ArtifactId("summary-1"),
                    ContentRole =
                        EvidencePackageContentRoles
                            .GeneratedOrganizationalMaterial
                }
            ]
        };

        Assert.Equal(
            [
                "Package: package-1",
                "Claim Issue: issue-1",
                "Purpose: Physician reviewer package",
                "Reviewer Role: MedicalProfessional",
                "Artifacts: 2",
                "- UnderlyingEvidence: source-1",
                "- GeneratedOrganizationalMaterial: summary-1"
            ],
            VeteransEvidencePackageFormatter.Format(details));
    }
}
