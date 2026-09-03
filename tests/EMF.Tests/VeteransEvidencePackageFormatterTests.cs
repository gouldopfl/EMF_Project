using EMF.ConsoleApplication;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;

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

    [Fact]
    public void Format_ReviewerPackageIncludesResolvedArtifactDetails()
    {
        var packageId = new EvidencePackageId("package-1");

        var details =
            new VeteransReviewerPackageDetails
            {
                PackageDetails =
                    new EvidencePackageDetails
                    {
                        Package = new EvidencePackage
                        {
                            Id = packageId,
                            ClaimIssueId =
                                new ClaimIssueId("issue-1"),
                            Purpose =
                                "Physician reviewer package",
                            ReviewerRole =
                                "MedicalProfessional"
                        },
                        Artifacts =
                        [
                            new EvidencePackageArtifact
                            {
                                EvidencePackageId = packageId,
                                ArtifactId =
                                    new ArtifactId("source-1"),
                                ContentRole =
                                    EvidencePackageContentRoles
                                        .UnderlyingEvidence
                            }
                        ]
                    },
                Artifacts =
                [
                    new Artifact
                    {
                        Id = new ArtifactId("source-1"),
                        Name = "Sleep Study",
                        ArtifactType = "medical-record"
                    }
                ],
                ArtifactContents =
                [
                    new VeteransReviewerArtifactContent
                    {
                        Artifact =
                            new Artifact
                            {
                                Id = new ArtifactId("source-1"),
                                Name = "Sleep Study",
                                ArtifactType = "medical-record"
                            },
                        Text = "Severe obstructive sleep apnea documented."
                    }
                ]
            };

        Assert.Equal(
            [
                "Package: package-1",
                "Claim Issue: issue-1",
                "Purpose: Physician reviewer package",
                "Reviewer Role: MedicalProfessional",
                "Artifacts: 1",
                "- UnderlyingEvidence: source-1",
                "",
                "Artifact Details:",
                "- source-1: Sleep Study [medical-record]",
                "",
                "Artifact Content:",
                "Severe obstructive sleep apnea documented."
            ],
            VeteransEvidencePackageFormatter.Format(details));
    }

}
