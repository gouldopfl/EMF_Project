using DocumentFormat.OpenXml.Packaging;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.Tests;

public sealed partial class VeteransReviewerPackageDocxRendererTests
{
    [Fact]
    public void Render_SeparatesReviewerMetadataFromInternalIdentity()
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
            "Medical Professional",
            text);

        var paragraphs =
            document.MainDocumentPart
                .Document!
                .Body!
                .Elements<
                    DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
                .Select(paragraph => paragraph.InnerText)
                .ToArray();

        Assert.DoesNotContain(
            "Claim Issue: issue-1",
            paragraphs);

        Assert.DoesNotContain(
            "Claim issue under review: issue-1",
            paragraphs);

        Assert.DoesNotContain(
            "Package Reference: package-1",
            paragraphs);

        Assert.DoesNotContain(
            "EMF Package Reference: package-1",
            paragraphs);

        Assert.DoesNotContain(
            "EMF Claim Issue Reference: issue-1",
            paragraphs);

        var traceabilityIndex =
            Array.IndexOf(
                paragraphs,
                "Appendix F — Evidence Traceability");

        Assert.True(traceabilityIndex >= 0);

        Assert.DoesNotContain(
            paragraphs[..traceabilityIndex],
            paragraph =>
                paragraph.Contains(
                    "MedicalProfessional",
                    StringComparison.Ordinal));

        Assert.DoesNotContain(
            "EMF Reviewer Role: MedicalProfessional",
            paragraphs);
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
                ArtifactType = "medical-record",
                CreatedUtc =
                    new DateTimeOffset(
                        2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
                Fingerprint =
                    new EMF.Core.Models.Integrity.ContentFingerprint
                    {
                        Algorithm = "SHA-256",
                        Value = "abc123"
                    },
                Metadata =
                    new Dictionary<string, object>
                    {
                        [VeteransArtifactMetadataKeys.SourceStartPage] = "1003",
                        [VeteransArtifactMetadataKeys.SourceEndPage] = "1005",
                        [VeteransArtifactMetadataKeys.NoteDate] = "2025-07-30",
                        [VeteransArtifactMetadataKeys.NoteTitle] = "PAP SET-UP CONSULT"
                    }
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
                            "Severe obstructive sleep apnea documented.",
                        Provenance =
                        [
                            new Provenance
                            {
                                ArtifactId = artifact.Id,
                                Source = "/records/sleep-study.pdf",
                                RecordedBy = "EMF.Discovery",
                                RecordedUtc =
                                    new DateTimeOffset(
                                        2026, 8, 1, 13, 0, 0,
                                        TimeSpan.Zero)
                            },
                            new Provenance
                            {
                                ArtifactId = artifact.Id,
                                Source = "EMF.Intelligence",
                                RecordedBy = "promoter",
                                RecordedUtc =
                                    new DateTimeOffset(
                                        2026, 8, 1, 15, 0, 0,
                                        TimeSpan.Zero),
                                Properties =
                                    new Dictionary<string, object>
                                    {
                                        ["reviewedBy"] = "reviewer",
                                        ["reviewedUtc"] =
                                            new DateTimeOffset(
                                                2026, 8, 1, 15, 5, 0,
                                                TimeSpan.Zero)
                                    }
                            }
                        ],
                        Relationships =
                        [
                            new Relationship
                            {
                                SourceArtifactId = artifact.Id,
                                TargetArtifactId =
                                    new ArtifactId("derived-1"),
                                RelationshipType = "DerivedFrom",
                                CreatedUtc =
                                    new DateTimeOffset(
                                        2026, 8, 1, 14, 0, 0,
                                        TimeSpan.Zero)
                            }
                        ]
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

        Assert.Contains("Category: Additional Evidence", text);
        Assert.Contains("Evidence Date: 2025-07-30", text);
        Assert.Contains("Source pages: 1003-1005", text);

        Assert.DoesNotContain("source-1", text);
        Assert.DoesNotContain("Artifact Type:", text);
        Assert.DoesNotContain("Created UTC:", text);
        Assert.DoesNotContain("Fingerprint:", text);
        Assert.DoesNotContain("Provenance:", text);
        Assert.DoesNotContain("Relationship:", text);
        Assert.DoesNotContain("Promoted By:", text);
        Assert.DoesNotContain("Reviewed By:", text);

        Assert.Contains(
            "Severe obstructive sleep apnea documented.",
            text);
    }

    [Fact]
    public void Render_RejectsMissingReviewerArtifactContent()
    {
        var packageId =
            new EvidencePackageId("package-1");

        var artifactId =
            new ArtifactId("source-1");

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
                                ArtifactId = artifactId,
                                ContentRole =
                                    EvidencePackageContentRoles
                                        .UnderlyingEvidence
                            }
                        ]
                    },
                Artifacts = [],
                ArtifactContents = []
            };

        var exception =
            Assert.Throws<InvalidOperationException>(
                () =>
                    VeteransReviewerPackageDocxRenderer.Render(
                        details));

        Assert.Contains(
            packageId.Value,
            exception.Message);

        Assert.Contains(
            artifactId.Value,
            exception.Message);
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

        Assert.DoesNotContain(
            EvidencePackageContentRoles.UnderlyingEvidence,
            text);

        Assert.DoesNotContain(
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
            "Executive Evidence Summary";

        const string evidenceHeading =
            "Evidence Index";

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
                    "CONFIDENTIAL — VETERAN MEDICAL INFORMATION");

        Assert.Contains(
            paragraphs,
            paragraph =>
                paragraph.InnerText ==
                    "Veterans Evidence Reviewer Report");

        Assert.Contains(
            paragraphs,
            paragraph =>
                paragraph.InnerText ==
                    "Executive Evidence Summary");

        Assert.Contains(
            paragraphs,
            paragraph =>
                paragraph.InnerText ==
                    "Issues Presented for Medical Review");

        Assert.Contains(
            paragraphs,
            paragraph =>
                paragraph.InnerText ==
                    "Questions for the Reviewing Physician");
    }


    [Fact]
    public void Render_IncludesChronologyAndMedicalLiteratureSections()
    {
        var packageId =
            new EvidencePackageId("package-1");

        var medical =
            new Artifact
            {
                Id = new ArtifactId("medical-1"),
                Name = "clinical-note.txt",
                ArtifactType = "medical-record",
                Metadata =
                    new Dictionary<string, object>
                    {
                        [VeteransArtifactMetadataKeys.NoteDate] = "2021-05-18",
                        [VeteransArtifactMetadataKeys.NoteTitle] =
                            "SLEEP MED PAP SET-UP CONSULT RESULT",
                        [VeteransArtifactMetadataKeys.EvidenceDate] =
                            "2020-04-03",
                        [VeteransArtifactMetadataKeys.EvidenceTitle] =
                            "Generic Evidence Title",
                        [VeteransArtifactMetadataKeys.SourceStartPage] = "3265",
                        [VeteransArtifactMetadataKeys.SourceEndPage] = "3267"
                    }
            };

        var undatedMedical =
            new Artifact
            {
                Id = new ArtifactId("medical-undated"),
                Name = "undated.pdf",
                ArtifactType = "medical-record",
                Metadata =
                    new Dictionary<string, object>
                    {
                        [VeteransArtifactMetadataKeys.EvidenceTitle] =
                            "Undated Medical Evidence"
                    }
            };

        var literature =
            new Artifact
            {
                Id = new ArtifactId("literature-1"),
                Name = "study.html",
                ArtifactType = "file"
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
                                ArtifactId = medical.Id,
                                ContentRole =
                                    EvidencePackageContentRoles
                                        .UnderlyingEvidence
                            },
                            new EvidencePackageArtifact
                            {
                                EvidencePackageId = packageId,
                                ArtifactId = undatedMedical.Id,
                                ContentRole =
                                    EvidencePackageContentRoles
                                        .UnderlyingEvidence
                            },
                            new EvidencePackageArtifact
                            {
                                EvidencePackageId = packageId,
                                ArtifactId = literature.Id,
                                ContentRole =
                                    EvidencePackageContentRoles
                                        .UnderlyingEvidence
                            }
                        ]
                    },
                Artifacts = [medical, undatedMedical, literature],
                ArtifactContents =
                [
                    new VeteransReviewerArtifactContent
                    {
                        Artifact = medical,
                        Text = "Clinical evidence.",
                        Appendix =
                            VeteransReviewerPackageAppendix
                                .MedicalEvidence
                    },
                    new VeteransReviewerArtifactContent
                    {
                        Artifact = undatedMedical,
                        Text = "Undated medical evidence.",
                        Appendix =
                            VeteransReviewerPackageAppendix
                                .MedicalEvidence
                    },
                    new VeteransReviewerArtifactContent
                    {
                        Artifact = literature,
                        Text =
                            "Published OSA Study\nAbstract text.",
                        Appendix =
                            VeteransReviewerPackageAppendix
                                .MedicalLiterature,
                        ReviewedMedicalLiteratureClassifications =
                        [
                            new ReviewedMedicalLiteratureClassification
                            {
                                Association =
                                    new RequirementMedicalLiterature
                                    {
                                        RequirementId =
                                            new RequirementId(
                                                "requirement-reviewed-lit"),
                                        MedicalLiteratureSourceId =
                                            new MedicalLiteratureSourceId(
                                                "study-reviewed-lit"),
                                        GuidanceRole =
                                            EvidenceGuidanceRoles
                                                .SupportsRequirement,
                                        Description =
                                            "Supports the medical mechanism."
                                    },
                                ArtifactId = literature.Id,
                                PromotedBy = "promotion-test",
                                PromotedUtc =
                                    new DateTimeOffset(
                                        2026, 9, 12, 12, 5, 0,
                                        TimeSpan.Zero),
                                ReviewedBy = "reviewer@example.test",
                                ReviewedUtc =
                                    new DateTimeOffset(
                                        2026, 9, 12, 12, 0, 0,
                                        TimeSpan.Zero),
                                IntelligenceOutput = "{}",
                                CapabilityId = "TextStructuredExtraction",
                                ProviderId = "test-provider",
                                CorrelationId = "docx-reviewed-lit",
                                EngineName = "test-engine",
                                StartedUtc =
                                    new DateTimeOffset(
                                        2026, 9, 12, 11, 58, 0,
                                        TimeSpan.Zero),
                                CompletedUtc =
                                    new DateTimeOffset(
                                        2026, 9, 12, 11, 59, 0,
                                        TimeSpan.Zero),
                                RequiresReview = true,
                                Warnings = [],
                                SourceExcerpts =
                                [
                                    new MedicalLiteratureSourceExcerpt
                                    {
                                        ArtifactId = literature.Id,
                                        Text =
                                            "Exact accepted source excerpt."
                                    }
                                ]
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
            document.MainDocumentPart!.Document!.InnerText;

        Assert.Contains(
            "Key Evidence and Chronology",
            text);

        Assert.Contains(
            "2020-04-03 — Generic Evidence Title",
            text);

        Assert.Contains(
            "Medical / Scientific Literature Considered",
            text);

        Assert.Contains(
            "Published OSA Study",
            text);

        Assert.Contains(
            "Role: Supports Requirement",
            text);

        Assert.Contains(
            "Relevance: Supports the medical mechanism.",
            text);

        Assert.Contains(
            "Accepted Source Excerpt:",
            text);

        Assert.Contains(
            "Exact accepted source excerpt.",
            text);

        var paragraphs =
            document.MainDocumentPart!
                .Document!
                .Body!
                .Elements<
                    DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
                .Select(paragraph => paragraph.InnerText)
                .ToArray();

        var traceabilityIndex =
            Array.IndexOf(
                paragraphs,
                "Appendix F — Evidence Traceability");

        Assert.True(traceabilityIndex >= 0);

        var reviewerFacingParagraphs =
            paragraphs[..traceabilityIndex];

        Assert.DoesNotContain(
            reviewerFacingParagraphs,
            paragraph =>
                paragraph.Contains(
                    "requirement-reviewed-lit",
                    StringComparison.Ordinal));

        Assert.DoesNotContain(
            reviewerFacingParagraphs,
            paragraph =>
                paragraph.Contains(
                    EvidenceGuidanceRoles.SupportsRequirement,
                    StringComparison.Ordinal));

        Assert.DoesNotContain(
            reviewerFacingParagraphs,
            paragraph =>
                paragraph.Contains(
                    "reviewer@example.test",
                    StringComparison.Ordinal));

        Assert.DoesNotContain(
            reviewerFacingParagraphs,
            paragraph =>
                paragraph.Contains(
                    "2026-09-12T12:00:00.0000000+00:00",
                    StringComparison.Ordinal));

        Assert.DoesNotContain("requirement-reviewed-lit", text);
        Assert.DoesNotContain("study-reviewed-lit", text);
        Assert.DoesNotContain(
            EvidenceGuidanceRoles.SupportsRequirement,
            text);
        Assert.DoesNotContain("promotion-test", text);
        Assert.DoesNotContain("reviewer@example.test", text);
        Assert.DoesNotContain(
            "2026-09-12T12:00:00.0000000+00:00",
            text);
        Assert.DoesNotContain(
            "2026-09-12T12:05:00.0000000+00:00",
            text);

        Assert.True(
            text.IndexOf("1. Generic Evidence Title", StringComparison.Ordinal) <
            text.IndexOf("2. Undated Medical Evidence", StringComparison.Ordinal));
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
                            "Veterans Evidence Reviewer Report"));

        var sectionHeading =
            Assert.Single(
                paragraphs.Where(
                    paragraph =>
                        paragraph.InnerText ==
                            "Executive Evidence Summary"));

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
    public void Render_AddsConfidentialPageNumberFooter()
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

        var mainPart =
            Assert.IsType<MainDocumentPart>(
                document.MainDocumentPart);

        var footerPart =
            Assert.Single(mainPart.FooterParts);

        var footer =
            Assert.IsType<
                DocumentFormat.OpenXml.Wordprocessing.Footer>(
                    footerPart.Footer);

        Assert.Contains(
            "CONFIDENTIAL — VETERAN MEDICAL INFORMATION",
            footer.InnerText);

        Assert.Contains(
            "Veterans Evidence Reviewer Report",
            footer.InnerText);

        var fieldInstructions =
            footer
                .Descendants<
                    DocumentFormat.OpenXml.Wordprocessing.SimpleField>()
                .Select(field => field.Instruction?.Value?.Trim())
                .ToArray();

        Assert.Contains("PAGE", fieldInstructions);
        Assert.Contains("NUMPAGES", fieldInstructions);

        var settingsPart =
            Assert.IsType<DocumentSettingsPart>(
                mainPart.DocumentSettingsPart);

        var updateFields =
            Assert.Single(
                settingsPart.Settings!
                    .Elements<
                        DocumentFormat.OpenXml.Wordprocessing.UpdateFieldsOnOpen>());

        Assert.True(updateFields.Val?.Value);

        var mainDocument =
            Assert.IsType<
                DocumentFormat.OpenXml.Wordprocessing.Document>(
                    mainPart.Document);

        var sectionProperties =
            Assert.Single(
                mainDocument
                    .Body!
                    .Elements<
                        DocumentFormat.OpenXml.Wordprocessing.SectionProperties>());

        var footerReference =
            Assert.Single(
                sectionProperties.Elements<
                    DocumentFormat.OpenXml.Wordprocessing.FooterReference>());

        Assert.Equal(
            mainPart.GetIdOfPart(footerPart),
            footerReference.Id?.Value);
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
                            "Veterans Evidence Reviewer Report"));

        var sectionHeading =
            Assert.Single(
                paragraphs.Where(
                    paragraph =>
                        paragraph.InnerText ==
                            "Executive Evidence Summary"));

        Assert.Equal(
            "360",
            title.ParagraphProperties?
                .SpacingBetweenLines?
                .After?
                .Value);

        Assert.Equal(
            "180",
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
                    .TakeWhile(
                        paragraph =>
                            paragraph.InnerText !=
                            "Appendix F — Evidence Traceability")
                    .Where(
                        paragraph =>
                            paragraph.InnerText ==
                            "Sleep Study"));

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
                    .TakeWhile(
                        paragraph =>
                            paragraph.InnerText !=
                            "Appendix F — Evidence Traceability")
                    .Where(
                        paragraph =>
                            paragraph.InnerText ==
                            "Sleep Study"));

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
                            "Purpose:",
                            StringComparison.Ordinal) ||
                        paragraph.InnerText.StartsWith(
                            "Reviewer Role:",
                            StringComparison.Ordinal))
                .ToArray();

        Assert.Equal(
            2,
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
            2,
            metadata.Length);

        Assert.All(
            metadata,
            paragraph =>
                Assert.Equal(
                    "60",
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
    public void Render_ReflowsWrappedArtifactContent()
    {
        var details =
            CreatePrintableDetails(
                [],
                """
                SLEEP STAFF COMMENTS:
                compliance is good. mask leak is high possibly elevating ahi. fit him
                with
                the dw nasal mask. he states the f30i leaks too much around the nose.
                he
                also has a beard.

                FOLLOW UP:
                Patient to return to PAP clinic for follow-up in
                1
                month.
                """);

        var content =
            VeteransReviewerPackageDocxRenderer.Render(
                details);

        using var stream =
            new MemoryStream(content);

        using var document =
            WordprocessingDocument.Open(
                stream,
                false);

        var body =
            document.MainDocumentPart!
                .Document!
                .Body!;

        var paragraphs =
            body.Elements<
                    DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
                .ToArray();

        Assert.Contains(
            paragraphs,
            paragraph =>
                paragraph.InnerText ==
                    "compliance is good. mask leak is high possibly elevating ahi. " +
                    "fit him with the dw nasal mask. he states the f30i leaks too much " +
                    "around the nose. he also has a beard.");

        var evidenceParagraph =
            Assert.Single(
                paragraphs.Where(
                    paragraph =>
                        paragraph.InnerText ==
                            "compliance is good. mask leak is high possibly elevating ahi. " +
                            "fit him with the dw nasal mask. he states the f30i leaks too much " +
                            "around the nose. he also has a beard."));

        Assert.Empty(
            evidenceParagraph.Descendants<
                DocumentFormat.OpenXml.Wordprocessing.Break>());
    }


    [Fact]
    public void Render_KeepsClinicalHeadingsWithFollowingContent()
    {
        var details =
            CreatePrintableDetails(
                [],
                """
                SLEEP MED PAP CLINIC NOTE
                Details
                Date entered: November 15, 2021
                Location: RICHARD L. ROUDEBUSH VAMC
                """);

        var content =
            VeteransReviewerPackageDocxRenderer.Render(
                details);

        using var stream =
            new MemoryStream(content);

        using var document =
            WordprocessingDocument.Open(
                stream,
                false);

        var paragraphs =
            document.MainDocumentPart!
                .Document!
                .Body!
                .Elements<
                    DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
                .ToArray();

        var heading =
            Assert.Single(
                paragraphs.Where(
                    paragraph =>
                        paragraph.InnerText ==
                            "SLEEP MED PAP CLINIC NOTE"));

        var detailsParagraph =
            Assert.Single(
                paragraphs.Where(
                    paragraph =>
                        paragraph.InnerText == "Details"));

        Assert.NotNull(
            heading.ParagraphProperties?.KeepNext);

        Assert.NotNull(
            detailsParagraph.ParagraphProperties?.KeepNext);
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

        Assert.NotEmpty(headings);

        Assert.All(
            headings,
            paragraph =>
                Assert.NotNull(
                    paragraph.ParagraphProperties?
                        .KeepNext));
    }


    [Fact]
    public void Render_StartsUnderlyingEvidenceOnNewPage()
    {
        var packageId = new EvidencePackageId("package-1");

        var summary = new Artifact
        {
            Id = new ArtifactId("summary-1"),
            Name = "Summary",
            ArtifactType = "text-summary"
        };

        var evidence = new Artifact
        {
            Id = new ArtifactId("source-1"),
            Name = "Evidence",
            ArtifactType = "medical-record"
        };

        var details = new VeteransReviewerPackageDetails
        {
            PackageDetails = new EvidencePackageDetails
            {
                Package = new EvidencePackage
                {
                    Id = packageId,
                    ClaimIssueId = new ClaimIssueId("issue-1"),
                    Purpose = "Reviewer package",
                    ReviewerRole = "MedicalProfessional"
                },
                Artifacts =
                [
                    new()
                    {
                        EvidencePackageId = packageId,
                        ArtifactId = summary.Id,
                        ContentRole = EvidencePackageContentRoles.GeneratedOrganizationalMaterial
                    },
                    new()
                    {
                        EvidencePackageId = packageId,
                        ArtifactId = evidence.Id,
                        ContentRole = EvidencePackageContentRoles.UnderlyingEvidence
                    }
                ]
            },
            Artifacts = [summary, evidence],
            ArtifactContents =
            [
                new() { Artifact = summary, Text = "Summary." },
                new() { Artifact = evidence, Text = "Evidence." }
            ]
        };

        var bytes = VeteransReviewerPackageDocxRenderer.Render(details);

        using var stream = new MemoryStream(bytes);
        using var document = WordprocessingDocument.Open(stream, false);

        var heading = document.MainDocumentPart!.Document!.Body!
            .Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
            .Single(x => x.InnerText == "Evidence Index");

        Assert.NotNull(
            heading.ParagraphProperties?.PageBreakBefore);
    }

}

public sealed partial class VeteransReviewerPackageDocxRendererTests
{
    [Fact]
    public void Render_GroupsUnderlyingEvidenceByAppendix()
    {
        var packageId =
            new EvidencePackageId("package-1");

        var artifact =
            new Artifact
            {
                Id = new ArtifactId("artifact-1"),
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
                                Purpose = "Medical review",
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
                        Text = "Sleep study evidence.",
                        Appendix =
                            VeteransReviewerPackageAppendix
                                .MedicalEvidence
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

        var mainPart =
            Assert.IsType<MainDocumentPart>(
                document.MainDocumentPart);

        var documentRoot =
            mainPart.Document;

        Assert.NotNull(documentRoot);

        var body =
            documentRoot!.Body;

        Assert.NotNull(body);

        var text =
            body!.InnerText;

        Assert.Contains(
            "Appendix A — Medical Evidence",
            text);
    }
}

public sealed partial class VeteransReviewerPackageDocxRendererTests
{
    [Fact]
    public void Render_StartsEachArtifactAfterTheFirstOnANewPage()
    {
        var packageId = new EvidencePackageId("package-artifacts");
        var first = new Artifact
        {
            Id = new ArtifactId("artifact-1"),
            Name = "First Evidence",
            ArtifactType = "medical-record"
        };
        var second = new Artifact
        {
            Id = new ArtifactId("artifact-2"),
            Name = "Second Evidence",
            ArtifactType = "medical-record"
        };

        var details = new VeteransReviewerPackageDetails
        {
            PackageDetails = new EvidencePackageDetails
            {
                Package = new EvidencePackage
                {
                    Id = packageId,
                    ClaimIssueId = new ClaimIssueId("issue-1"),
                    Purpose = "Medical review",
                    ReviewerRole = "MedicalProfessional"
                },
                Artifacts =
                [
                    new EvidencePackageArtifact
                    {
                        EvidencePackageId = packageId,
                        ArtifactId = first.Id,
                        ContentRole = EvidencePackageContentRoles.UnderlyingEvidence
                    },
                    new EvidencePackageArtifact
                    {
                        EvidencePackageId = packageId,
                        ArtifactId = second.Id,
                        ContentRole = EvidencePackageContentRoles.UnderlyingEvidence
                    }
                ]
            },
            Artifacts = [first, second],
            ArtifactContents =
            [
                new VeteransReviewerArtifactContent
                {
                    Artifact = first,
                    Text = "First evidence paragraph.",
                    Appendix = VeteransReviewerPackageAppendix.MedicalEvidence
                },
                new VeteransReviewerArtifactContent
                {
                    Artifact = second,
                    Text = "Second evidence paragraph.",
                    Appendix = VeteransReviewerPackageAppendix.MedicalEvidence
                }
            ]
        };

        var bytes = VeteransReviewerPackageDocxRenderer.Render(details);

        using var stream = new MemoryStream(bytes);
        using var document = WordprocessingDocument.Open(stream, false);

        var secondHeading =
            Assert.Single(
                document.MainDocumentPart!
                    .Document!
                    .Body!
                    .Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
                    .Where(paragraph =>
                        paragraph.InnerText == "Second Evidence" &&
                        paragraph.ParagraphProperties?
                            .ParagraphStyleId?
                            .Val?
                            .Value == "Heading2" &&
                        paragraph.ParagraphProperties?
                            .PageBreakBefore is not null));

        Assert.NotNull(
            secondHeading.ParagraphProperties?.PageBreakBefore);
    }

    [Fact]
    public void Render_KeepsReviewerParagraphLinesTogether()
    {
        var details =
            CreatePrintableDetails(
                [],
                "This is a reviewer paragraph that should stay together.");

        var bytes = VeteransReviewerPackageDocxRenderer.Render(details);

        using var stream = new MemoryStream(bytes);
        using var document = WordprocessingDocument.Open(stream, false);

        var paragraph =
            Assert.Single(
                document.MainDocumentPart!
                    .Document!
                    .Body!
                    .Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
                    .Where(item =>
                        item.InnerText ==
                            "This is a reviewer paragraph that should stay together."));

        Assert.NotNull(paragraph.ParagraphProperties?.KeepLines);
    }
}

public sealed partial class VeteransReviewerPackageDocxRendererTests
{
    [Fact]
    public void Render_EmbedsPrintableSourcePageAsImagePart()
    {
        var details =
            CreatePrintableDetails(
            [
                new PrintableArtifactPage
                {
                    PageNumber = 1,
                    ContentType = "image/png",
                    Content = TinyPng()
                }
            ],
            "Derived evidence text.");

        var bytes =
            VeteransReviewerPackageDocxRenderer.Render(details);

        using var stream = new MemoryStream(bytes);
        using var document =
            WordprocessingDocument.Open(stream, false);

        var mainPart =
            Assert.IsType<MainDocumentPart>(
                document.MainDocumentPart);

        Assert.Single(mainPart.ImageParts);

        var text = mainPart.Document!.InnerText;

        Assert.Contains("Source Page 1", text);
        Assert.DoesNotContain("Extracted Text (Derived):", text);
        Assert.DoesNotContain("Derived evidence text.", text);

        var sourcePage =
            Assert.Single(
                mainPart.Document!
                    .Body!
                    .Elements<
                        DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
                    .Where(
                        paragraph =>
                            paragraph.InnerText == "Source Page 1"));

        Assert.NotNull(
            sourcePage.ParagraphProperties?.KeepNext);
    }

    [Fact]
    public void Render_ReservesSpaceOnEveryPrintableImagePage()
    {
        var details =
            CreatePrintableDetails(
            [
                new PrintableArtifactPage
                {
                    PageNumber = 1,
                    ContentType = "image/png",
                    Content = TallPng()
                },
                new PrintableArtifactPage
                {
                    PageNumber = 2,
                    ContentType = "image/png",
                    Content = TallPng()
                }
            ],
            "");

        var bytes =
            VeteransReviewerPackageDocxRenderer.Render(details);

        using var stream = new MemoryStream(bytes);
        using var document =
            WordprocessingDocument.Open(stream, false);

        var extents =
            document.MainDocumentPart!
                .Document!
                .Descendants<
                    DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent>()
                .ToArray();

        Assert.Equal(2, extents.Length);
        Assert.Equal(6_400_800L, extents[0].Cy?.Value);
        Assert.Equal(6_400_800L, extents[1].Cy?.Value);
    }

    [Fact]
    public void Render_PreservesPrintableSourcePageOrder()
    {
        var details =
            CreatePrintableDetails(
            [
                new PrintableArtifactPage
                {
                    PageNumber = 1,
                    ContentType = "image/png",
                    Content = TinyPng()
                },
                new PrintableArtifactPage
                {
                    PageNumber = 2,
                    ContentType = "image/png",
                    Content = TinyPng()
                }
            ],
            "");

        var bytes =
            VeteransReviewerPackageDocxRenderer.Render(details);

        using var stream = new MemoryStream(bytes);
        using var document =
            WordprocessingDocument.Open(stream, false);

        var mainPart =
            Assert.IsType<MainDocumentPart>(
                document.MainDocumentPart);

        Assert.Equal(2, mainPart.ImageParts.Count());

        var text = mainPart.Document!.InnerText;

        Assert.True(
            text.IndexOf("Source Page 1", StringComparison.Ordinal) <
            text.IndexOf("Source Page 2", StringComparison.Ordinal));

        var paragraphs =
            mainPart.Document!
                .Body!
                .Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
                .ToArray();

        var secondPageIndex =
            Array.FindIndex(
                paragraphs,
                paragraph =>
                    paragraph.InnerText == "Source Page 2");

        Assert.True(secondPageIndex > 0);

        Assert.Contains(
            paragraphs[secondPageIndex - 1].Descendants<DocumentFormat.OpenXml.Wordprocessing.Break>(),
            lineBreak => lineBreak.Type?.Value == DocumentFormat.OpenXml.Wordprocessing.BreakValues.Page);
    }
}

public sealed partial class VeteransReviewerPackageDocxRendererTests
{
    [Fact]
    public void Render_RejectsUnsupportedPrintablePageType()
    {
        var details =
            CreatePrintableDetails(
            [
                new PrintableArtifactPage
                {
                    PageNumber = 1,
                    ContentType = "image/jpeg",
                    Content = TinyPng()
                }
            ],
            "");

        Assert.Throws<NotSupportedException>(
            () => VeteransReviewerPackageDocxRenderer.Render(details));
    }

    [Fact]
    public void Render_RejectsMalformedPrintablePng()
    {
        var details =
            CreatePrintableDetails(
            [
                new PrintableArtifactPage
                {
                    PageNumber = 1,
                    ContentType = "image/png",
                    Content = new byte[] { 1, 2, 3 }
                }
            ],
            "");

        Assert.Throws<InvalidDataException>(
            () => VeteransReviewerPackageDocxRenderer.Render(details));
    }

    [Fact]
    public void Render_RejectsNonSequentialPrintablePages()
    {
        var details =
            CreatePrintableDetails(
            [
                new PrintableArtifactPage
                {
                    PageNumber = 2,
                    ContentType = "image/png",
                    Content = TinyPng()
                }
            ],
            "");

        Assert.Throws<InvalidOperationException>(
            () => VeteransReviewerPackageDocxRenderer.Render(details));
    }
}

public sealed partial class VeteransReviewerPackageDocxRendererTests
{
    [Fact]
    public void Render_RemovesInvalidXmlControlCharacters()
    {
        var details =
            CreatePrintableDetails(
                [],
                "Before\fAfter\uFFFDBeyond");

        var bytes =
            VeteransReviewerPackageDocxRenderer.Render(details);

        using var stream = new MemoryStream(bytes);
        using var document =
            WordprocessingDocument.Open(stream, false);

        var text =
            document.MainDocumentPart!.Document!.InnerText;

        Assert.Contains("Before After Beyond", text);
        Assert.DoesNotContain("\uFFFD", text);
        Assert.DoesNotContain("\f", text);
    }
}

public sealed partial class VeteransReviewerPackageDocxRendererTests
{
    private static VeteransReviewerPackageDetails CreatePrintableDetails(
        IReadOnlyList<PrintableArtifactPage> pages,
        string text)
    {
        var packageId =
            new EvidencePackageId("package-print");

        var artifact =
            new Artifact
            {
                Id = new ArtifactId("source-print"),
                Name = "Source Evidence",
                ArtifactType = "medical-record"
            };

        return new VeteransReviewerPackageDetails
        {
            PackageDetails =
                new EvidencePackageDetails
                {
                    Package =
                        new EvidencePackage
                        {
                            Id = packageId,
                            ClaimIssueId =
                                new ClaimIssueId("issue-print"),
                            Purpose = "Reviewer package",
                            ReviewerRole = "MedicalProfessional"
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
                    Text = text,
                    PrintablePages = pages
                }
            ]
        };
    }

    private static byte[] TinyPng() =>
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwC" +
            "AAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private static byte[] TallPng() =>
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAoAAABkCAAAAACap/5L" +
            "AAAAGElEQVR4nGP8zwADTAyjzFHmKHOUSSYTAPj8AceRbvek" +
            "AAAAAElFTkSuQmCC");
}

public sealed partial class VeteransReviewerPackageDocxRendererTests
{
    [Fact]
    public void Render_LabelsMedicalLiteratureAppendix()
    {
        var packageId = new EvidencePackageId("package-literature");
        var artifact = new Artifact
        {
            Id = new ArtifactId("artifact-literature"),
            Name = "study.pdf",
            ArtifactType = "pdf"
        };

        var details = new VeteransReviewerPackageDetails
        {
            PackageDetails = new EvidencePackageDetails
            {
                Package = new EvidencePackage
                {
                    Id = packageId,
                    ClaimIssueId = new ClaimIssueId("issue-1"),
                    Purpose = "Medical review",
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
                    Text = "Published study.",
                    Appendix =
                        VeteransReviewerPackageAppendix.MedicalLiterature
                }
            ]
        };

        var bytes = VeteransReviewerPackageDocxRenderer.Render(details);

        using var stream = new MemoryStream(bytes);
        using var document =
            WordprocessingDocument.Open(stream, false);

        Assert.Contains(
            "Appendix E — Medical / Scientific Literature",
            document.MainDocumentPart!.Document!.InnerText);
    }
}

public sealed partial class VeteransReviewerPackageDocxRendererTests
{
    [Fact]
    public void Render_RejectsArtifactAssociatedWithAnotherPackage()
    {
        var artifact = IntegrityArtifact();
        var details =
            CreateIntegrityDetails(
            [
                new EvidencePackageArtifact
                {
                    EvidencePackageId =
                        new EvidencePackageId("package-other"),
                    ArtifactId = artifact.Id,
                    ContentRole =
                        EvidencePackageContentRoles.UnderlyingEvidence
                }
            ],
            []);

        var exception =
            Assert.Throws<InvalidOperationException>(
                () => VeteransReviewerPackageDocxRenderer.Render(details));

        Assert.Contains("associated with package 'package-other'", exception.Message);
    }

    [Fact]
    public void Render_RejectsUnsupportedPackageContentRole()
    {
        var artifact = IntegrityArtifact();
        var packageId = new EvidencePackageId("package-integrity");
        var details =
            CreateIntegrityDetails(
            [
                new EvidencePackageArtifact
                {
                    EvidencePackageId = packageId,
                    ArtifactId = artifact.Id,
                    ContentRole = "UnexpectedRole"
                }
            ],
            []);

        var exception =
            Assert.Throws<InvalidOperationException>(
                () => VeteransReviewerPackageDocxRenderer.Render(details));

        Assert.Contains("unsupported content role 'UnexpectedRole'", exception.Message);
    }

    [Fact]
    public void Render_RejectsConflictingPackageContentRoles()
    {
        var artifact = IntegrityArtifact();
        var packageId = new EvidencePackageId("package-integrity");
        var details =
            CreateIntegrityDetails(
            [
                new EvidencePackageArtifact
                {
                    EvidencePackageId = packageId,
                    ArtifactId = artifact.Id,
                    ContentRole =
                        EvidencePackageContentRoles.UnderlyingEvidence
                },
                new EvidencePackageArtifact
                {
                    EvidencePackageId = packageId,
                    ArtifactId = artifact.Id,
                    ContentRole =
                        EvidencePackageContentRoles
                            .GeneratedOrganizationalMaterial
                }
            ],
            []);

        var exception =
            Assert.Throws<InvalidOperationException>(
                () => VeteransReviewerPackageDocxRenderer.Render(details));

        Assert.Contains("conflicting content roles", exception.Message);
        Assert.Contains(artifact.Id.Value, exception.Message);
    }

    [Fact]
    public void Render_RejectsDuplicateReviewerArtifactContent()
    {
        var artifact = IntegrityArtifact();
        var packageId = new EvidencePackageId("package-integrity");
        var content = IntegrityContent(artifact);
        var details =
            CreateIntegrityDetails(
            [
                new EvidencePackageArtifact
                {
                    EvidencePackageId = packageId,
                    ArtifactId = artifact.Id,
                    ContentRole =
                        EvidencePackageContentRoles.UnderlyingEvidence
                }
            ],
            [content, content]);

        var exception =
            Assert.Throws<InvalidOperationException>(
                () => VeteransReviewerPackageDocxRenderer.Render(details));

        Assert.Contains("multiple reviewable content entries", exception.Message);
        Assert.Contains(artifact.Id.Value, exception.Message);
    }

    [Fact]
    public void Render_RejectsReviewerContentOutsidePackage()
    {
        var artifact = IntegrityArtifact();
        var details =
            CreateIntegrityDetails(
                [],
                [IntegrityContent(artifact)]);

        var exception =
            Assert.Throws<InvalidOperationException>(
                () => VeteransReviewerPackageDocxRenderer.Render(details));

        Assert.Contains("not part of the package", exception.Message);
        Assert.Contains(artifact.Id.Value, exception.Message);
    }

    [Fact]
    public void Render_RejectsMismatchedReviewerProvenanceArtifact()
    {
        var artifact = IntegrityArtifact();
        var packageId = new EvidencePackageId("package-integrity");
        var foreignArtifactId = new ArtifactId("source-other");
        var content =
            new VeteransReviewerArtifactContent
            {
                Artifact = artifact,
                Text = "Integrity test content.",
                Provenance =
                [
                    new Provenance
                    {
                        ArtifactId = foreignArtifactId,
                        Source = "test",
                        RecordedBy = "tester"
                    }
                ]
            };
        var details =
            CreateIntegrityDetails(
            [
                IntegrityPackageArtifact(packageId, artifact)
            ],
            [content]);

        var exception =
            Assert.Throws<InvalidOperationException>(
                () => VeteransReviewerPackageDocxRenderer.Render(details));

        Assert.Contains("provenance for artifact 'source-other'", exception.Message);
    }

    [Fact]
    public void Render_RejectsUnrelatedReviewerRelationship()
    {
        var artifact = IntegrityArtifact();
        var packageId = new EvidencePackageId("package-integrity");
        var content =
            new VeteransReviewerArtifactContent
            {
                Artifact = artifact,
                Text = "Integrity test content.",
                Relationships =
                [
                    new Relationship
                    {
                        SourceArtifactId = new ArtifactId("source-other"),
                        TargetArtifactId = new ArtifactId("target-other"),
                        RelationshipType = "DerivedFrom"
                    }
                ]
            };
        var details =
            CreateIntegrityDetails(
            [
                IntegrityPackageArtifact(packageId, artifact)
            ],
            [content]);

        var exception =
            Assert.Throws<InvalidOperationException>(
                () => VeteransReviewerPackageDocxRenderer.Render(details));

        Assert.Contains("unrelated artifact relationship", exception.Message);
    }

    [Fact]
    public void Render_RejectsMismatchedReviewedLiteratureArtifact()
    {
        var artifact = IntegrityArtifact();
        var packageId = new EvidencePackageId("package-integrity");
        var foreignArtifactId = new ArtifactId("source-other");
        var content =
            new VeteransReviewerArtifactContent
            {
                Artifact = artifact,
                Text = "Integrity test content.",
                ReviewedMedicalLiteratureClassifications =
                [
                    IntegrityReviewedLiterature(
                        foreignArtifactId,
                        foreignArtifactId)
                ]
            };
        var details =
            CreateIntegrityDetails(
            [
                IntegrityPackageArtifact(packageId, artifact)
            ],
            [content]);

        var exception =
            Assert.Throws<InvalidOperationException>(
                () => VeteransReviewerPackageDocxRenderer.Render(details));

        Assert.Contains("reviewed literature for artifact 'source-other'", exception.Message);
    }

    [Fact]
    public void Render_RejectsMismatchedReviewedLiteratureExcerptArtifact()
    {
        var artifact = IntegrityArtifact();
        var packageId = new EvidencePackageId("package-integrity");
        var content =
            new VeteransReviewerArtifactContent
            {
                Artifact = artifact,
                Text = "Integrity test content.",
                ReviewedMedicalLiteratureClassifications =
                [
                    IntegrityReviewedLiterature(
                        artifact.Id,
                        new ArtifactId("source-other"))
                ]
            };
        var details =
            CreateIntegrityDetails(
            [
                IntegrityPackageArtifact(packageId, artifact)
            ],
            [content]);

        var exception =
            Assert.Throws<InvalidOperationException>(
                () => VeteransReviewerPackageDocxRenderer.Render(details));

        Assert.Contains(
            "reviewed literature excerpt for artifact 'source-other'",
            exception.Message);
    }

    private static VeteransReviewerPackageDetails CreateIntegrityDetails(
        IReadOnlyList<EvidencePackageArtifact> packageArtifacts,
        IReadOnlyList<VeteransReviewerArtifactContent> artifactContents) =>
        new()
        {
            PackageDetails =
                new EvidencePackageDetails
                {
                    Package =
                        new EvidencePackage
                        {
                            Id = new EvidencePackageId("package-integrity"),
                            ClaimIssueId =
                                new ClaimIssueId("issue-integrity"),
                            Purpose = "Reviewer package",
                            ReviewerRole = "MedicalProfessional"
                        },
                    Artifacts = packageArtifacts
                },
            Artifacts =
                artifactContents
                    .Select(content => content.Artifact)
                    .ToArray(),
            ArtifactContents = artifactContents
        };

    private static EvidencePackageArtifact IntegrityPackageArtifact(
        EvidencePackageId packageId,
        Artifact artifact) =>
        new()
        {
            EvidencePackageId = packageId,
            ArtifactId = artifact.Id,
            ContentRole = EvidencePackageContentRoles.UnderlyingEvidence
        };

    private static ReviewedMedicalLiteratureClassification
        IntegrityReviewedLiterature(
            ArtifactId artifactId,
            ArtifactId excerptArtifactId)
    {
        var timestamp =
            new DateTimeOffset(
                2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

        return new ReviewedMedicalLiteratureClassification
        {
            Association =
                new RequirementMedicalLiterature
                {
                    RequirementId = new RequirementId("requirement-integrity"),
                    MedicalLiteratureSourceId =
                        new MedicalLiteratureSourceId("study-integrity"),
                    GuidanceRole = EvidenceGuidanceRoles.SupportsRequirement,
                    Description = "Integrity test literature."
                },
            ArtifactId = artifactId,
            PromotedBy = "tester",
            PromotedUtc = timestamp,
            ReviewedBy = "tester",
            ReviewedUtc = timestamp,
            IntelligenceOutput = "{}",
            CapabilityId = "integrity",
            ProviderId = "test",
            CorrelationId = "integrity",
            EngineName = "test",
            StartedUtc = timestamp,
            CompletedUtc = timestamp,
            RequiresReview = false,
            Warnings = [],
            SourceExcerpts =
            [
                new MedicalLiteratureSourceExcerpt
                {
                    ArtifactId = excerptArtifactId,
                    Text = "Integrity excerpt."
                }
            ]
        };
    }

    private static Artifact IntegrityArtifact() =>
        new()
        {
            Id = new ArtifactId("source-integrity"),
            Name = "Integrity Source",
            ArtifactType = "medical-record"
        };

    private static VeteransReviewerArtifactContent IntegrityContent(
        Artifact artifact) =>
        new()
        {
            Artifact = artifact,
            Text = "Integrity test content."
        };
}
