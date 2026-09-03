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


    [Fact]
    public void Render_IdentifiesArtifactContentRoles()
    {
        var packageId =
            new EvidencePackageId("package-1");

        var source =
            new Artifact
            {
                Id = new ArtifactId("source-1"),
                Name = "Sleep Study",
                ArtifactType = "medical-record"
            };

        var summary =
            new Artifact
            {
                Id = new ArtifactId("summary-1"),
                Name = "Reviewer Summary",
                ArtifactType = "text-summary"
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
                                ArtifactId = source.Id,
                                ContentRole =
                                    EvidencePackageContentRoles
                                        .UnderlyingEvidence
                            },
                            new EvidencePackageArtifact
                            {
                                EvidencePackageId = packageId,
                                ArtifactId = summary.Id,
                                ContentRole =
                                    EvidencePackageContentRoles
                                        .GeneratedOrganizationalMaterial
                            }
                        ]
                    },
                Artifacts =
                [
                    source,
                    summary
                ],
                ArtifactContents =
                [
                    new VeteransReviewerArtifactContent
                    {
                        Artifact = source,
                        Text = "Underlying medical evidence."
                    },
                    new VeteransReviewerArtifactContent
                    {
                        Artifact = summary,
                        Text = "Generated reviewer summary."
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
            EvidencePackageContentRoles.UnderlyingEvidence,
            text);

        Assert.Contains(
            EvidencePackageContentRoles.GeneratedOrganizationalMaterial,
            text);
    }


    [Fact]
    public void Render_GroupsReviewerContentByRole()
    {
        var packageId =
            new EvidencePackageId("package-1");

        var source =
            new Artifact
            {
                Id = new ArtifactId("source-1"),
                Name = "Sleep Study",
                ArtifactType = "medical-record"
            };

        var summary =
            new Artifact
            {
                Id = new ArtifactId("summary-1"),
                Name = "Reviewer Summary",
                ArtifactType = "text-summary"
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
                                ArtifactId = source.Id,
                                ContentRole =
                                    EvidencePackageContentRoles
                                        .UnderlyingEvidence
                            },
                            new EvidencePackageArtifact
                            {
                                EvidencePackageId = packageId,
                                ArtifactId = summary.Id,
                                ContentRole =
                                    EvidencePackageContentRoles
                                        .GeneratedOrganizationalMaterial
                            }
                        ]
                    },
                Artifacts =
                [
                    source,
                    summary
                ],
                ArtifactContents =
                [
                    new VeteransReviewerArtifactContent
                    {
                        Artifact = source,
                        Text = "Underlying medical evidence."
                    },
                    new VeteransReviewerArtifactContent
                    {
                        Artifact = summary,
                        Text = "Generated reviewer summary."
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

        const string generatedHeading =
            "Generated Organizational Material";

        const string evidenceHeading =
            "Underlying Evidence";

        Assert.Contains(
            generatedHeading,
            text);

        Assert.Contains(
            evidenceHeading,
            text);

        Assert.True(
            text.IndexOf(
                generatedHeading,
                StringComparison.Ordinal) <
            text.IndexOf(
                evidenceHeading,
                StringComparison.Ordinal));
    }


    [Fact]
    public void Render_UsesReviewerPackagePresentationHeadings()
    {
        var packageId =
            new EvidencePackageId("package-1");

        var summary =
            new Artifact
            {
                Id = new ArtifactId("summary-1"),
                Name = "Reviewer Summary",
                ArtifactType = "text-summary"
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
                                ArtifactId = summary.Id,
                                ContentRole =
                                    EvidencePackageContentRoles
                                        .GeneratedOrganizationalMaterial
                            }
                        ]
                    },
                Artifacts = [summary],
                ArtifactContents =
                [
                    new VeteransReviewerArtifactContent
                    {
                        Artifact = summary,
                        Text = "Generated reviewer summary."
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

        var paragraphs =
            document.MainDocumentPart
                .Document!
                .Body!
                .Elements<
                    DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
                .ToArray();

        Assert.Contains(
            paragraphs,
            paragraph =>
                paragraph.InnerText ==
                    "Veterans Evidence Reviewer Package");

        Assert.Contains(
            paragraphs,
            paragraph =>
                paragraph.InnerText ==
                    "Generated Organizational Material");
    }


    [Fact]
    public void Render_StylesDocumentAndSectionHeadings()
    {
        var packageId =
            new EvidencePackageId("package-1");

        var summary =
            new Artifact
            {
                Id = new ArtifactId("summary-1"),
                Name = "Reviewer Summary",
                ArtifactType = "text-summary"
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
                                ArtifactId = summary.Id,
                                ContentRole =
                                    EvidencePackageContentRoles
                                        .GeneratedOrganizationalMaterial
                            }
                        ]
                    },
                Artifacts = [summary],
                ArtifactContents =
                [
                    new VeteransReviewerArtifactContent
                    {
                        Artifact = summary,
                        Text = "Generated reviewer summary."
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

        var paragraphs =
            document.MainDocumentPart
                .Document!
                .Body!
                .Elements<
                    DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
                .ToArray();

        var title =
            Assert.Single(
                paragraphs.Where(
                    paragraph =>
                        paragraph.InnerText ==
                            "Veterans Evidence Reviewer Package"));

        var sectionHeading =
            Assert.Single(
                paragraphs.Where(
                    paragraph =>
                        paragraph.InnerText ==
                            "Generated Organizational Material"));

        Assert.Equal(
            "Title",
            title.ParagraphProperties?
                .ParagraphStyleId?
                .Val?
                .Value);

        Assert.Equal(
            "Heading1",
            sectionHeading.ParagraphProperties?
                .ParagraphStyleId?
                .Val?
                .Value);
    }


    [Fact]
    public void Render_UsesStandardPageMargins()
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
                Artifacts = [],
                ArtifactContents = []
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

        Assert.NotNull(
            document.MainDocumentPart
                .Document!
                .Body);

        var sectionProperties =
            Assert.Single(
                document.MainDocumentPart
                    .Document!
                    .Body!
                    .Elements<
                        DocumentFormat.OpenXml.Wordprocessing.SectionProperties>());

        var margins =
            sectionProperties.GetFirstChild<
                DocumentFormat.OpenXml.Wordprocessing.PageMargin>();

        Assert.NotNull(margins);

        Assert.Equal(
            1440,
            margins!.Top?.Value);

        Assert.Equal(
            1440U,
            margins.Right?.Value);

        Assert.Equal(
            1440,
            margins.Bottom?.Value);

        Assert.Equal(
            1440U,
            margins.Left?.Value);
    }


    [Fact]
    public void Render_SpacesDocumentAndSectionHeadings()
    {
        var packageId =
            new EvidencePackageId("package-1");

        var summary =
            new Artifact
            {
                Id = new ArtifactId("summary-1"),
                Name = "Reviewer Summary",
                ArtifactType = "text-summary"
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
                                ArtifactId = summary.Id,
                                ContentRole =
                                    EvidencePackageContentRoles
                                        .GeneratedOrganizationalMaterial
                            }
                        ]
                    },
                Artifacts = [summary],
                ArtifactContents =
                [
                    new VeteransReviewerArtifactContent
                    {
                        Artifact = summary,
                        Text = "Generated reviewer summary."
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

        var paragraphs =
            document.MainDocumentPart
                .Document!
                .Body!
                .Elements<
                    DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
                .ToArray();

        var title =
            Assert.Single(
                paragraphs.Where(
                    paragraph =>
                        paragraph.InnerText ==
                            "Veterans Evidence Reviewer Package"));

        var sectionHeading =
            Assert.Single(
                paragraphs.Where(
                    paragraph =>
                        paragraph.InnerText ==
                            "Generated Organizational Material"));

        Assert.Equal(
            "240",
            title.ParagraphProperties?
                .SpacingBetweenLines?
                .After?
                .Value);

        Assert.Equal(
            "120",
            sectionHeading.ParagraphProperties?
                .SpacingBetweenLines?
                .Before?
                .Value);

        Assert.Equal(
            "120",
            sectionHeading.ParagraphProperties?
                .SpacingBetweenLines?
                .After?
                .Value);
    }


    [Fact]
    public void Render_StylesArtifactHeadings()
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

        var artifactHeading =
            Assert.Single(
                document.MainDocumentPart
                    .Document!
                    .Body!
                    .Elements<
                        DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
                    .Where(
                        paragraph =>
                            paragraph.InnerText.Contains(
                                "Sleep Study",
                                StringComparison.Ordinal) &&
                            paragraph.InnerText.Contains(
                                "source-1",
                                StringComparison.Ordinal)));

        Assert.Equal(
            "Heading2",
            artifactHeading.ParagraphProperties?
                .ParagraphStyleId?
                .Val?
                .Value);
    }


    [Fact]
    public void Render_SpacesArtifactHeadings()
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

        var artifactHeading =
            Assert.Single(
                document.MainDocumentPart
                    .Document!
                    .Body!
                    .Elements<
                        DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
                    .Where(
                        paragraph =>
                            paragraph.InnerText.Contains(
                                "Sleep Study",
                                StringComparison.Ordinal) &&
                            paragraph.InnerText.Contains(
                                "source-1",
                                StringComparison.Ordinal)));

        Assert.Equal(
            "120",
            artifactHeading.ParagraphProperties?
                .SpacingBetweenLines?
                .Before?
                .Value);

        Assert.Equal(
            "60",
            artifactHeading.ParagraphProperties?
                .SpacingBetweenLines?
                .After?
                .Value);
    }


    [Fact]
    public void Render_StylesReviewerPackageMetadata()
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
                Artifacts = [],
                ArtifactContents = []
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

        var metadata =
            document.MainDocumentPart
                .Document!
                .Body!
                .Elements<
                    DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
                .Where(
                    paragraph =>
                        paragraph.InnerText.StartsWith(
                            "Package:",
                            StringComparison.Ordinal) ||
                        paragraph.InnerText.StartsWith(
                            "Claim Issue:",
                            StringComparison.Ordinal) ||
                        paragraph.InnerText.StartsWith(
                            "Purpose:",
                            StringComparison.Ordinal) ||
                        paragraph.InnerText.StartsWith(
                            "Reviewer Role:",
                            StringComparison.Ordinal))
                .ToArray();

        Assert.Equal(
            4,
            metadata.Length);

        Assert.All(
            metadata,
            paragraph =>
                Assert.Equal(
                    "Subtitle",
                    paragraph.ParagraphProperties?
                        .ParagraphStyleId?
                        .Val?
                        .Value));
    }


    [Fact]
    public void Render_SpacesReviewerPackageMetadata()
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
                Artifacts = [],
                ArtifactContents = []
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

        var metadata =
            document.MainDocumentPart
                .Document!
                .Body!
                .Elements<
                    DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
                .Where(
                    paragraph =>
                        paragraph.ParagraphProperties?
                            .ParagraphStyleId?
                            .Val?
                            .Value ==
                        "Subtitle")
                .ToArray();

        Assert.Equal(
            4,
            metadata.Length);

        Assert.All(
            metadata,
            paragraph =>
                Assert.Equal(
                    "40",
                    paragraph.ParagraphProperties?
                        .SpacingBetweenLines?
                        .After?
                        .Value));
    }


    [Fact]
    public void Render_SpacesArtifactContentParagraphs()
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

        var contentParagraph =
            Assert.Single(
                document.MainDocumentPart
                    .Document!
                    .Body!
                    .Elements<
                        DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
                    .Where(
                        paragraph =>
                            paragraph.InnerText ==
                                "Severe obstructive sleep apnea documented."));

        Assert.Equal(
            "60",
            contentParagraph.ParagraphProperties?
                .SpacingBetweenLines?
                .After?
                .Value);
    }


    [Fact]
    public void Render_PreservesArtifactContentLineBreaks()
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
                            "Diagnosis: Severe OSA\n" +
                            "Treatment: ASV"
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

        var contentParagraph =
            Assert.Single(
                document.MainDocumentPart
                    .Document!
                    .Body!
                    .Elements<
                        DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
                    .Where(
                        paragraph =>
                            paragraph.InnerText.Contains(
                                "Diagnosis: Severe OSA",
                                StringComparison.Ordinal)));

        Assert.Single(
            contentParagraph.Descendants<
                DocumentFormat.OpenXml.Wordprocessing.Break>());

        Assert.Contains(
            "Treatment: ASV",
            contentParagraph.InnerText);
    }


    [Fact]
    public void Render_KeepsHeadingsWithFollowingContent()
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

        var headings =
            document.MainDocumentPart
                .Document!
                .Body!
                .Elements<
                    DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
                .Where(
                    paragraph =>
                    {
                        var styleId =
                            paragraph.ParagraphProperties?
                                .ParagraphStyleId?
                                .Val?
                                .Value;

                        return styleId == "Heading1" ||
                               styleId == "Heading2";
                    })
                .ToArray();

        Assert.Equal(
            2,
            headings.Length);

        Assert.All(
            headings,
            paragraph =>
                Assert.NotNull(
                    paragraph.ParagraphProperties?
                        .KeepNext));
    }

}
