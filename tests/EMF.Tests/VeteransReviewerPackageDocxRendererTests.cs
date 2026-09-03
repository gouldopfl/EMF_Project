using DocumentFormat.OpenXml.Packaging;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.Tests;

public sealed class VeteransReviewerPackageDocxRendererTests
{
    [Fact]
    public void Render_IncludesReviewerPackageIdentity()
    {
        var packageId =
            new EvidencePackageId("package-1");

        var details =
            new VeteransReviewerPackageDetails
            {
                PackageDetails =
                    new EvidencePackageDetails
                    {
                        Package =
                            new EvidencePackage
                            {
                                Id = packageId,
                                ClaimIssueId =
                                    new ClaimIssueId("issue-1"),
                                Purpose =
                                    "Physician reviewer package",
                                ReviewerRole =
                                    "MedicalProfessional"
                            },
                        Artifacts = []
                    },
                Artifacts = []
            };

        var content =
            VeteransReviewerPackageDocxRenderer.Render(
                details);

        using var stream =
            new MemoryStream(content);

        using var document =
            WordprocessingDocument.Open(
                stream,
                false);

        Assert.NotNull(
            document.MainDocumentPart);

        Assert.NotNull(
            document.MainDocumentPart!.Document);

        var text =
            document.MainDocumentPart
                .Document!
                .InnerText;

        Assert.Contains(
            "Physician reviewer package",
            text);

        Assert.Contains(
            "issue-1",
            text);

        Assert.Contains(
            "MedicalProfessional",
            text);

        Assert.Contains(
            "package-1",
            text);
    }

    [Fact]
    public void Render_IncludesReviewerArtifactContent()
    {
        var packageId =
            new EvidencePackageId("package-1");

        var artifact =
            new Artifact
            {
                Id = new ArtifactId("source-1"),
                Name = "Sleep Study",
                ArtifactType = "medical-record"
            };

        var details =
            new VeteransReviewerPackageDetails
            {
                PackageDetails =
                    new EvidencePackageDetails
                    {
                        Package =
                            new EvidencePackage
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
                                ArtifactId = artifact.Id,
                                ContentRole =
                                    EvidencePackageContentRoles
                                        .UnderlyingEvidence
                            }
                        ]
                    },
                Artifacts = [artifact],
                ArtifactContents =
                [
                    new VeteransReviewerArtifactContent
                    {
                        Artifact = artifact,
                        Text =
                            "Severe obstructive sleep apnea documented."
                    }
                ]
            };

        var content =
            VeteransReviewerPackageDocxRenderer.Render(
                details);

        using var stream =
            new MemoryStream(content);

        using var document =
            WordprocessingDocument.Open(
                stream,
                false);

        Assert.NotNull(
            document.MainDocumentPart);

        Assert.NotNull(
            document.MainDocumentPart!.Document);

        var text =
            document.MainDocumentPart
                .Document!
                .InnerText;

        Assert.Contains(
            "Sleep Study",
            text);

        Assert.Contains(
            "source-1",
            text);

        Assert.Contains(
            "Severe obstructive sleep apnea documented.",
            text);
    }

}
