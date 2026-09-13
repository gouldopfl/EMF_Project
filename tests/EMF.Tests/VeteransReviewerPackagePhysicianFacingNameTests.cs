using DocumentFormat.OpenXml.Packaging;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.Tests;

public sealed class VeteransReviewerPackagePhysicianFacingNameTests
{
    [Fact]
    public void Render_DoesNotExposeRawEvidenceFileName()
    {
        const string fileName = "private-sleep-study-source.pdf";
        var packageId = new EvidencePackageId("package-filename");

        var artifact = new Artifact
        {
            Id = new ArtifactId("artifact-filename"),
            Name = fileName,
            ArtifactType = "file"
        };

        var details = new VeteransReviewerPackageDetails
        {
            PackageDetails = new EvidencePackageDetails
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
                        ArtifactId = artifact.Id,
                        ContentRole =
                            EvidencePackageContentRoles.UnderlyingEvidence
                    }
                ]
            },
            Artifacts = [artifact],
            ArtifactContents =
            [
                new VeteransReviewerArtifactContent
                {
                    Artifact = artifact,
                    Text = "Sleep study evidence.",
                    Appendix =
                        VeteransReviewerPackageAppendix.MedicalEvidence
                }
            ]
        };

        var bytes = VeteransReviewerPackageDocxRenderer.Render(details);

        using var stream = new MemoryStream(bytes);
        using var document =
            WordprocessingDocument.Open(stream, false);

        var text =
            document.MainDocumentPart!.Document!.Body!.InnerText;

        Assert.DoesNotContain(fileName, text);
        Assert.Contains("Medical Evidence", text);
    }

    [Fact]
    public void Render_DoesNotExposeSupersessionStatus()
    {
        var packageId = new EvidencePackageId("package-version");
        var current = new Artifact
        {
            Id = new ArtifactId("statement-v2"),
            Name = "Veteran Personal Statement",
            ArtifactType = "file"
        };

        var details = new VeteransReviewerPackageDetails
        {
            PackageDetails = new EvidencePackageDetails
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
                        ArtifactId = current.Id,
                        ContentRole =
                            EvidencePackageContentRoles.UnderlyingEvidence
                    }
                ]
            },
            Artifacts = [current],
            ArtifactContents =
            [
                new VeteransReviewerArtifactContent
                {
                    Artifact = current,
                    Text = "Current statement.",
                    Appendix =
                        VeteransReviewerPackageAppendix.LayEvidence,
                    Relationships =
                    [
                        new Relationship
                        {
                            SourceArtifactId = current.Id,
                            TargetArtifactId =
                                new ArtifactId("statement-v1"),
                            RelationshipType = "Supersedes"
                        }
                    ]
                }
            ]
        };

        var bytes =
            VeteransReviewerPackageDocxRenderer.Render(details);

        using var stream = new MemoryStream(bytes);
        using var document =
            WordprocessingDocument.Open(stream, false);

        var text =
            document.MainDocumentPart!.Document!.Body!.InnerText;

        Assert.DoesNotContain("Version Status", text);
        Assert.DoesNotContain(
            "supersedes prior version",
            text,
            StringComparison.OrdinalIgnoreCase);
    }

}
