using EMF.Persistence.Repositories;
using EMF.Core.Models.Identities;
using EMF.Core.Models;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Extensions.VeteransClaims.Regulatory;

namespace EMF.Tests;

public sealed class VeteransClaimsSqliteMedicalLiteratureRepositoryTests
{
    [Fact]
    public async Task Repository_RoundTripsSourceAndRequirementLink()
    {
        var path = Path.GetTempFileName();

        try
        {
            var regulatory = new SqliteRegulatoryRepository(path);
            var repository = new SqliteMedicalLiteratureRepository(path);

            await repository.InitializeAsync();

            var authority = new RegulatoryAuthority
            {
                Id = new RegulatoryAuthorityId("authority-medlit"),
                AuthorityType = "Regulation",
                Citation = "38 CFR",
                Title = "Veterans Affairs"
            };

            await regulatory.AddRegulatoryAuthorityAsync(authority);

            var provision = new RegulatoryProvision
            {
                Id = new RegulatoryProvisionId("provision-medlit"),
                RegulatoryAuthorityId = authority.Id,
                ProvisionType = RegulatoryProvisionTypes.Requirement,
                Citation = "38 CFR 3.310"
            };

            await regulatory.AddRegulatoryProvisionAsync(provision);

            var requirement = new Requirement
            {
                Id = new RequirementId("requirement-medlit"),
                RegulatoryProvisionId = provision.Id,
                Description = "Secondary service connection requirement."
            };

            await regulatory.AddRequirementAsync(requirement);

            var source = new MedicalLiteratureSource
            {
                Id = new MedicalLiteratureSourceId("study-001"),
                Title = "Example medical study",
                Authors = "Example Authors",
                Publication = "Example Journal",
                PublicationYear = 2026,
                VaAffiliated = true,
                VaFunded = true,
                PeerReviewed = true,
                FundingSource = "U.S. Department of Veterans Affairs",
                ResearchOrganization = "VA Research",
                Doi = "10.1000/example",
                Pmid = "12345678",
                SourceUri = "https://example.test/study",
                SourceHash = "sha256:example",
                RetrievedUtc = DateTimeOffset.UtcNow
            };

            await repository.AddMedicalLiteratureSourceAsync(source);

            await repository.AddRequirementMedicalLiteratureAsync(
                new RequirementMedicalLiterature
                {
                    RequirementId = requirement.Id,
                    MedicalLiteratureSourceId = source.Id,
                    GuidanceRole = EvidenceGuidanceRoles.SupportsRequirement,
                    Description = "Supports the medical mechanism."
                });

            var stored =
                await repository.GetMedicalLiteratureSourceAsync(source.Id);

            Assert.NotNull(stored);
            Assert.Equal(source.Id, stored!.Id);
            Assert.Equal(source.Title, stored.Title);
            Assert.True(stored.VaAffiliated);
            Assert.True(stored.VaFunded);
            Assert.True(stored.PeerReviewed);
            Assert.Equal(
                source.FundingSource,
                stored.FundingSource);
            Assert.Equal(
                source.ResearchOrganization,
                stored.ResearchOrganization);
            Assert.Equal(source.Doi, stored.Doi);
            Assert.Equal(source.Pmid, stored.Pmid);

            var links =
                await repository.GetRequirementMedicalLiteratureAsync(
                    requirement.Id);

            var link = Assert.Single(links);

            Assert.Equal(source.Id, link.MedicalLiteratureSourceId);
            Assert.Equal(
                EvidenceGuidanceRoles.SupportsRequirement,
                link.GuidanceRole);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ArtifactAssociation_RoundTripsInBothDirections()
    {
        var path = Path.GetTempFileName();

        try
        {
            var literature = new SqliteMedicalLiteratureRepository(path);
            await literature.InitializeAsync();

            var evidence = new SqliteEvidenceRepository(path);
            await evidence.InitializeAsync();

            var source = new MedicalLiteratureSource
            {
                Id = new MedicalLiteratureSourceId("study-artifact"),
                Title = "Example study",
                Authors = "Example Authors",
                Publication = "Example Journal",
                VaAffiliated = false,
                VaFunded = false,
                PeerReviewed = true
            };

            await literature.AddMedicalLiteratureSourceAsync(source);

            var artifact = new Artifact
            {
                Id = new ArtifactId("artifact-literature"),
                Name = "Example study.pdf",
                ArtifactType = "pdf"
            };

            await evidence.AddArtifactAsync(artifact);

            await literature.AddMedicalLiteratureSourceArtifactAsync(
                new MedicalLiteratureSourceArtifact
                {
                    MedicalLiteratureSourceId = source.Id,
                    ArtifactId = artifact.Id
                });

            Assert.Equal(
                artifact.Id,
                Assert.Single(
                    await literature.GetArtifactIdsAsync(source.Id)));

            Assert.Equal(
                source.Id,
                Assert.Single(
                    await literature.GetMedicalLiteratureSourceIdsAsync(
                        artifact.Id)));
        }
        finally
        {
            File.Delete(path);
        }
    }
    [Fact]
    public async Task ReviewedClassification_RoundTripsWithAcceptedLink()
    {
        var path = Path.GetTempFileName();

        try
        {
            var regulatory = new SqliteRegulatoryRepository(path);
            var literature = new SqliteMedicalLiteratureRepository(path);
            var evidence = new SqliteEvidenceRepository(path);

            await literature.InitializeAsync();
            await evidence.InitializeAsync();

            var authority = new RegulatoryAuthority
            {
                Id = new RegulatoryAuthorityId("authority-reviewed-lit"),
                AuthorityType = "Regulation",
                Citation = "38 CFR",
                Title = "Veterans Affairs"
            };

            await regulatory.AddRegulatoryAuthorityAsync(authority);

            var provision = new RegulatoryProvision
            {
                Id = new RegulatoryProvisionId("provision-reviewed-lit"),
                RegulatoryAuthorityId = authority.Id,
                ProvisionType = RegulatoryProvisionTypes.Requirement,
                Citation = "38 CFR 3.310"
            };

            await regulatory.AddRegulatoryProvisionAsync(provision);

            var requirement = new Requirement
            {
                Id = new RequirementId("requirement-reviewed-lit"),
                RegulatoryProvisionId = provision.Id,
                Description = "Secondary service connection requirement."
            };

            await regulatory.AddRequirementAsync(requirement);

            var source = new MedicalLiteratureSource
            {
                Id = new MedicalLiteratureSourceId("study-reviewed"),
                Title = "Reviewed study",
                Authors = "Example Authors",
                Publication = "Example Journal",
                VaAffiliated = false,
                VaFunded = false,
                PeerReviewed = true
            };

            await literature.AddMedicalLiteratureSourceAsync(source);

            var artifact = new Artifact
            {
                Id = new ArtifactId("artifact-reviewed-lit"),
                Name = "reviewed-study.pdf",
                ArtifactType = "pdf"
            };

            await evidence.AddArtifactAsync(artifact);

            await literature.AddMedicalLiteratureSourceArtifactAsync(
                new MedicalLiteratureSourceArtifact
                {
                    MedicalLiteratureSourceId = source.Id,
                    ArtifactId = artifact.Id
                });

            var promotedUtc =
                new DateTimeOffset(
                    2026, 9, 12, 13, 0, 0, TimeSpan.Zero);
            var reviewedUtc = promotedUtc.AddMinutes(-5);
            var startedUtc = reviewedUtc.AddMinutes(-2);
            var completedUtc = reviewedUtc.AddMinutes(-1);

            var classification =
                new ReviewedMedicalLiteratureClassification
                {
                    Association =
                        new RequirementMedicalLiterature
                        {
                            RequirementId = requirement.Id,
                            MedicalLiteratureSourceId = source.Id,
                            GuidanceRole =
                                EvidenceGuidanceRoles.SupportsRequirement,
                            Description =
                                "Supports the medical mechanism."
                        },
                    ArtifactId = artifact.Id,
                    PromotedBy = "review-promotion-test",
                    PromotedUtc = promotedUtc,
                    ReviewedBy = "reviewer@example.test",
                    ReviewedUtc = reviewedUtc,
                    IntelligenceOutput = """{"classifications":1}""",
                    CapabilityId = "TextStructuredExtraction",
                    ProviderId = "test-provider",
                    CorrelationId = "reviewed-lit-correlation",
                    EngineName = "test-engine",
                    EngineVersion = "1",
                    ProviderOperationId = "operation-1",
                    StartedUtc = startedUtc,
                    CompletedUtc = completedUtc,
                    RequiresReview = true,
                    Warnings = ["review required"],
                    SourceExcerpts =
                    [
                        new MedicalLiteratureSourceExcerpt
                        {
                            ArtifactId = artifact.Id,
                            Text = "Exact supporting source excerpt.",
                            StartOffset = 12,
                            Length = 32
                        }
                    ]
                };

            await literature.AddReviewedClassificationAsync(
                classification);

            var accepted =
                Assert.Single(
                    await literature
                        .GetRequirementMedicalLiteratureAsync(
                            requirement.Id));

            Assert.Equal(
                classification.Association.Description,
                accepted.Description);

            var stored =
                Assert.Single(
                    await literature.GetReviewedClassificationsAsync(
                        requirement.Id));

            var storedByArtifact =
                Assert.Single(
                    await literature.GetReviewedClassificationsAsync(
                        artifact.Id));

            Assert.Equal(
                stored.CorrelationId,
                storedByArtifact.CorrelationId);
            Assert.Equal(artifact.Id, stored.ArtifactId);
            Assert.Equal(classification.PromotedBy, stored.PromotedBy);
            Assert.Equal(classification.PromotedUtc, stored.PromotedUtc);
            Assert.Equal(classification.ReviewedBy, stored.ReviewedBy);
            Assert.Equal(classification.ReviewedUtc, stored.ReviewedUtc);
            Assert.Equal(
                classification.IntelligenceOutput,
                stored.IntelligenceOutput);
            Assert.Equal(
                classification.CapabilityId,
                stored.CapabilityId);
            Assert.Equal(classification.ProviderId, stored.ProviderId);
            Assert.Equal(
                classification.CorrelationId,
                stored.CorrelationId);
            Assert.Equal(classification.EngineName, stored.EngineName);
            Assert.Equal(
                classification.EngineVersion,
                stored.EngineVersion);
            Assert.Equal(
                classification.ProviderOperationId,
                stored.ProviderOperationId);
            Assert.Equal(classification.StartedUtc, stored.StartedUtc);
            Assert.Equal(
                classification.CompletedUtc,
                stored.CompletedUtc);
            Assert.True(stored.RequiresReview);
            Assert.Equal(classification.Warnings, stored.Warnings);

            var excerpt = Assert.Single(stored.SourceExcerpts);

            Assert.Equal(artifact.Id, excerpt.ArtifactId);
            Assert.Equal(
                "Exact supporting source excerpt.",
                excerpt.Text);
            Assert.Equal(12, excerpt.StartOffset);
            Assert.Equal(32, excerpt.Length);

            var secondReview =
                CopyReviewedClassification(
                    classification,
                    "reviewed-lit-correlation-second");

            var duplicateException =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => literature.AddReviewedClassificationAsync(
                        secondReview));

            Assert.Contains(
                "Explicit supersession is required",
                duplicateException.Message,
                StringComparison.Ordinal);

            Assert.Single(
                await literature.GetReviewedClassificationsAsync(
                    requirement.Id));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReviewedClassification_RollsBackLinkWhenArtifactUnlinked()
    {
        var path = Path.GetTempFileName();

        try
        {
            var regulatory = new SqliteRegulatoryRepository(path);
            var literature = new SqliteMedicalLiteratureRepository(path);

            await literature.InitializeAsync();

            var authority = new RegulatoryAuthority
            {
                Id = new RegulatoryAuthorityId("authority-rollback-lit"),
                AuthorityType = "Regulation",
                Citation = "38 CFR",
                Title = "Veterans Affairs"
            };

            await regulatory.AddRegulatoryAuthorityAsync(authority);

            var provision = new RegulatoryProvision
            {
                Id = new RegulatoryProvisionId("provision-rollback-lit"),
                RegulatoryAuthorityId = authority.Id,
                ProvisionType = RegulatoryProvisionTypes.Requirement,
                Citation = "38 CFR 3.310"
            };

            await regulatory.AddRegulatoryProvisionAsync(provision);

            var requirement = new Requirement
            {
                Id = new RequirementId("requirement-rollback-lit"),
                RegulatoryProvisionId = provision.Id,
                Description = "Secondary service connection requirement."
            };

            await regulatory.AddRequirementAsync(requirement);

            var source = new MedicalLiteratureSource
            {
                Id = new MedicalLiteratureSourceId("study-rollback"),
                Title = "Rollback study",
                Authors = "Example Authors",
                Publication = "Example Journal",
                VaAffiliated = false,
                VaFunded = false,
                PeerReviewed = true
            };

            await literature.AddMedicalLiteratureSourceAsync(source);

            var artifactId = new ArtifactId("artifact-not-linked");
            var reviewedUtc =
                new DateTimeOffset(
                    2026, 9, 12, 13, 0, 0, TimeSpan.Zero);

            var classification =
                new ReviewedMedicalLiteratureClassification
                {
                    Association =
                        new RequirementMedicalLiterature
                        {
                            RequirementId = requirement.Id,
                            MedicalLiteratureSourceId = source.Id,
                            GuidanceRole =
                                EvidenceGuidanceRoles.SupportsRequirement,
                            Description = "Should roll back."
                        },
                    ArtifactId = artifactId,
                    PromotedBy = "review-promotion-test",
                    PromotedUtc = reviewedUtc.AddMinutes(1),
                    ReviewedBy = "reviewer@example.test",
                    ReviewedUtc = reviewedUtc,
                    IntelligenceOutput = "{}",
                    CapabilityId = "TextStructuredExtraction",
                    ProviderId = "test-provider",
                    CorrelationId = "rollback-correlation",
                    EngineName = "test-engine",
                    StartedUtc = reviewedUtc.AddMinutes(-2),
                    CompletedUtc = reviewedUtc.AddMinutes(-1),
                    RequiresReview = true,
                    Warnings = [],
                    SourceExcerpts =
                    [
                        new MedicalLiteratureSourceExcerpt
                        {
                            ArtifactId = artifactId,
                            Text = "Unlinked excerpt."
                        }
                    ]
                };

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => literature.AddReviewedClassificationAsync(
                    classification));

            Assert.Empty(
                await literature.GetRequirementMedicalLiteratureAsync(
                    requirement.Id));
            Assert.Empty(
                await literature.GetReviewedClassificationsAsync(
                    requirement.Id));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReviewedClassifications_BatchRollsBackWhenLaterItemFails()
    {
        var path = Path.GetTempFileName();

        try
        {
            var regulatory = new SqliteRegulatoryRepository(path);
            var literature = new SqliteMedicalLiteratureRepository(path);
            var evidence = new SqliteEvidenceRepository(path);

            await literature.InitializeAsync();
            await evidence.InitializeAsync();

            var authority = new RegulatoryAuthority
            {
                Id = new RegulatoryAuthorityId("authority-batch-lit"),
                AuthorityType = "Regulation",
                Citation = "38 CFR",
                Title = "Veterans Affairs"
            };
            await regulatory.AddRegulatoryAuthorityAsync(authority);

            var provision = new RegulatoryProvision
            {
                Id = new RegulatoryProvisionId("provision-batch-lit"),
                RegulatoryAuthorityId = authority.Id,
                ProvisionType = RegulatoryProvisionTypes.Requirement,
                Citation = "38 CFR 3.310"
            };
            await regulatory.AddRegulatoryProvisionAsync(provision);

            var requirement = new Requirement
            {
                Id = new RequirementId("requirement-batch-lit"),
                RegulatoryProvisionId = provision.Id,
                Description = "Secondary service connection requirement."
            };
            await regulatory.AddRequirementAsync(requirement);

            var source = new MedicalLiteratureSource
            {
                Id = new MedicalLiteratureSourceId("study-batch-lit"),
                Title = "Batch study",
                Authors = "Example Authors",
                Publication = "Example Journal",
                VaAffiliated = false,
                VaFunded = false,
                PeerReviewed = true
            };
            await literature.AddMedicalLiteratureSourceAsync(source);

            var linkedArtifact = new Artifact
            {
                Id = new ArtifactId("artifact-batch-linked"),
                Name = "linked-study.pdf",
                ArtifactType = "pdf"
            };
            var unlinkedArtifact = new Artifact
            {
                Id = new ArtifactId("artifact-batch-unlinked"),
                Name = "unlinked-study.pdf",
                ArtifactType = "pdf"
            };

            await evidence.AddArtifactAsync(linkedArtifact);
            await evidence.AddArtifactAsync(unlinkedArtifact);
            await literature.AddMedicalLiteratureSourceArtifactAsync(
                new MedicalLiteratureSourceArtifact
                {
                    MedicalLiteratureSourceId = source.Id,
                    ArtifactId = linkedArtifact.Id
                });

            var reviewedUtc =
                new DateTimeOffset(
                    2026, 9, 12, 15, 0, 0, TimeSpan.Zero);

            ReviewedMedicalLiteratureClassification Create(
                ArtifactId artifactId,
                string role,
                string correlationId,
                string excerptText) =>
                new()
                {
                    Association = new RequirementMedicalLiterature
                    {
                        RequirementId = requirement.Id,
                        MedicalLiteratureSourceId = source.Id,
                        GuidanceRole = role,
                        Description = $"Accepted {role}."
                    },
                    ArtifactId = artifactId,
                    PromotedBy = "batch-promotion-test",
                    PromotedUtc = reviewedUtc.AddMinutes(1),
                    ReviewedBy = "reviewer@example.test",
                    ReviewedUtc = reviewedUtc,
                    IntelligenceOutput = "{}",
                    CapabilityId = "TextStructuredExtraction",
                    ProviderId = "test-provider",
                    CorrelationId = correlationId,
                    EngineName = "test-engine",
                    StartedUtc = reviewedUtc.AddMinutes(-2),
                    CompletedUtc = reviewedUtc.AddMinutes(-1),
                    RequiresReview = true,
                    Warnings = [],
                    SourceExcerpts =
                    [
                        new MedicalLiteratureSourceExcerpt
                        {
                            ArtifactId = artifactId,
                            Text = excerptText,
                            StartOffset = 0,
                            Length = excerptText.Length
                        }
                    ]
                };

            var classifications =
                new[]
                {
                    Create(
                        linkedArtifact.Id,
                        EvidenceGuidanceRoles.SupportsRequirement,
                        "batch-correlation-linked",
                        "Linked accepted excerpt."),
                    Create(
                        unlinkedArtifact.Id,
                        EvidenceGuidanceRoles.Corroborates,
                        "batch-correlation-unlinked",
                        "Unlinked accepted excerpt.")
                };

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => literature.AddReviewedClassificationsAsync(
                    classifications));

            Assert.Empty(
                await literature.GetRequirementMedicalLiteratureAsync(
                    requirement.Id));
            Assert.Empty(
                await literature.GetReviewedClassificationsAsync(
                    requirement.Id));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReviewedClassification_RejectsInvalidPromotionTimestamps()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteMedicalLiteratureRepository(path);

            var completedUtc =
                new DateTimeOffset(
                    2026, 9, 12, 14, 0, 0, TimeSpan.Zero);

            var classification =
                CreateReviewedClassificationForValidation(
                    completedUtc: completedUtc,
                    reviewedUtc: completedUtc.AddSeconds(-1));

            var exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => repository.AddReviewedClassificationAsync(
                        classification));

            Assert.Contains(
                "timestamps are invalid",
                exception.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReviewedClassification_RejectsIncompleteExcerptProvenance()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteMedicalLiteratureRepository(path);

            var classification =
                CreateReviewedClassificationForValidation(
                    startOffset: null,
                    length: null);

            var exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => repository.AddReviewedClassificationAsync(
                        classification));

            Assert.Contains(
                "excerpt provenance is invalid",
                exception.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReviewedClassification_RejectsMissingReviewIdentity()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteMedicalLiteratureRepository(path);

            var classification =
                CreateReviewedClassificationForValidation(
                    reviewedBy: " ");

            var exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => repository.AddReviewedClassificationAsync(
                        classification));

            Assert.Contains(
                "promotion provenance is incomplete",
                exception.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static ReviewedMedicalLiteratureClassification
        CopyReviewedClassification(
            ReviewedMedicalLiteratureClassification source,
            string correlationId) =>
        new()
        {
            Association = source.Association,
            ArtifactId = source.ArtifactId,
            PromotedBy = source.PromotedBy,
            PromotedUtc = source.PromotedUtc,
            ReviewedBy = source.ReviewedBy,
            ReviewedUtc = source.ReviewedUtc,
            IntelligenceOutput = source.IntelligenceOutput,
            CapabilityId = source.CapabilityId,
            ProviderId = source.ProviderId,
            CorrelationId = correlationId,
            EngineName = source.EngineName,
            EngineVersion = source.EngineVersion,
            ProviderOperationId = source.ProviderOperationId,
            StartedUtc = source.StartedUtc,
            CompletedUtc = source.CompletedUtc,
            RequiresReview = source.RequiresReview,
            Warnings = source.Warnings,
            SourceExcerpts = source.SourceExcerpts
        };

    private static ReviewedMedicalLiteratureClassification
        CreateReviewedClassificationForValidation(
            DateTimeOffset? completedUtc = null,
            DateTimeOffset? reviewedUtc = null,
            string reviewedBy = "reviewer@example.test",
            int? startOffset = 0,
            int? length = 17)
    {
        const string excerptText = "Accepted excerpt.";

        var started =
            new DateTimeOffset(
                2026, 9, 12, 13, 58, 0, TimeSpan.Zero);
        var completed =
            completedUtc ?? started.AddMinutes(1);
        var reviewed =
            reviewedUtc ?? completed.AddMinutes(1);

        return new ReviewedMedicalLiteratureClassification
        {
            Association =
                new RequirementMedicalLiterature
                {
                    RequirementId =
                        new RequirementId("validation-requirement"),
                    MedicalLiteratureSourceId =
                        new MedicalLiteratureSourceId("validation-source"),
                    GuidanceRole =
                        EvidenceGuidanceRoles.SupportsRequirement,
                    Description = "Accepted relevance."
                },
            ArtifactId = new ArtifactId("validation-artifact"),
            PromotedBy = "validation-promoter",
            PromotedUtc = reviewed.AddMinutes(1),
            ReviewedBy = reviewedBy,
            ReviewedUtc = reviewed,
            IntelligenceOutput = """{"classification":"accepted"}""",
            CapabilityId = "TextStructuredExtraction",
            ProviderId = "validation-provider",
            CorrelationId = "validation-correlation",
            EngineName = "validation-engine",
            EngineVersion = "1",
            ProviderOperationId = "validation-operation",
            StartedUtc = started,
            CompletedUtc = completed,
            RequiresReview = true,
            Warnings = [],
            SourceExcerpts =
            [
                new MedicalLiteratureSourceExcerpt
                {
                    ArtifactId =
                        new ArtifactId("validation-artifact"),
                    Text = excerptText,
                    StartOffset = startOffset,
                    Length = length
                }
            ]
        };
    }


    [Fact]
    public async Task ReviewedClassifications_SupersessionReplacesActiveReviewAndPreservesHistory()
    {
        var path = Path.GetTempFileName();

        try
        {
            var regulatory = new SqliteRegulatoryRepository(path);
            var literature = new SqliteMedicalLiteratureRepository(path);
            var evidence = new SqliteEvidenceRepository(path);

            await literature.InitializeAsync();
            await evidence.InitializeAsync();

            var authority = new RegulatoryAuthority
            {
                Id = new("authority-supersede-lit"),
                AuthorityType = "Regulation",
                Citation = "38 CFR",
                Title = "Veterans Affairs"
            };
            await regulatory.AddRegulatoryAuthorityAsync(authority);
            var provision = new RegulatoryProvision
            {
                Id = new("provision-supersede-lit"),
                RegulatoryAuthorityId = authority.Id,
                ProvisionType = RegulatoryProvisionTypes.Requirement,
                Citation = "38 CFR 3.310"
            };
            await regulatory.AddRegulatoryProvisionAsync(provision);
            var requirement = new Requirement
            {
                Id = new("requirement-supersede-lit"),
                RegulatoryProvisionId = provision.Id,
                Description = "Secondary service connection requirement."
            };
            await regulatory.AddRequirementAsync(requirement);

            var source = new MedicalLiteratureSource
            {
                Id = new("study-supersede-lit"),
                Title = "Superseded study",
                Authors = "Example Authors",
                Publication = "Example Journal",
                PeerReviewed = true
            };
            await literature.AddMedicalLiteratureSourceAsync(source);
            var artifact = new Artifact
            {
                Id = new("artifact-supersede-lit"),
                Name = "supersede-study.pdf",
                ArtifactType = "pdf"
            };
            await evidence.AddArtifactAsync(artifact);
            await literature.AddMedicalLiteratureSourceArtifactAsync(
                new MedicalLiteratureSourceArtifact
                {
                    MedicalLiteratureSourceId = source.Id,
                    ArtifactId = artifact.Id
                });

            var firstReviewedUtc =
                new DateTimeOffset(2026, 9, 12, 17, 0, 0, TimeSpan.Zero);

            ReviewedMedicalLiteratureClassification Create(
                string correlationId,
                string reviewer,
                string description,
                string excerpt,
                DateTimeOffset reviewedUtc) =>
                new()
                {
                    Association = new RequirementMedicalLiterature
                    {
                        RequirementId = requirement.Id,
                        MedicalLiteratureSourceId = source.Id,
                        GuidanceRole = EvidenceGuidanceRoles.SupportsRequirement,
                        Description = description
                    },
                    ArtifactId = artifact.Id,
                    PromotedBy = "supersession-test",
                    PromotedUtc = reviewedUtc.AddMinutes(1),
                    ReviewedBy = reviewer,
                    ReviewedUtc = reviewedUtc,
                    IntelligenceOutput = "{}",
                    CapabilityId = "TextStructuredExtraction",
                    ProviderId = "test-provider",
                    CorrelationId = correlationId,
                    EngineName = "test-engine",
                    StartedUtc = reviewedUtc.AddMinutes(-2),
                    CompletedUtc = reviewedUtc.AddMinutes(-1),
                    RequiresReview = true,
                    Warnings = [],
                    SourceExcerpts =
                    [
                        new MedicalLiteratureSourceExcerpt
                        {
                            ArtifactId = artifact.Id,
                            Text = excerpt,
                            StartOffset = 0,
                            Length = excerpt.Length
                        }
                    ]
                };

            var original = Create(
                "superseded-correlation",
                "first-reviewer@example.test",
                "Initial accepted relevance.",
                "Initial accepted excerpt.",
                firstReviewedUtc);
            await literature.AddReviewedClassificationAsync(original);

            var replacement = Create(
                "replacement-correlation",
                "second-reviewer@example.test",
                "Updated accepted relevance.",
                "Updated accepted excerpt.",
                firstReviewedUtc.AddHours(1));

            await literature.SupersedeReviewedClassificationsAsync(
                original.CorrelationId,
                [replacement]);

            var active = Assert.Single(
                await literature.GetReviewedClassificationsAsync(
                    requirement.Id));
            Assert.Equal(replacement.CorrelationId, active.CorrelationId);
            Assert.Equal(
                replacement.Association.Description,
                active.Association.Description);
            Assert.Equal(
                replacement.SourceExcerpts[0].Text,
                Assert.Single(active.SourceExcerpts).Text);

            await using var connection =
                VeteransClaimsSqliteConnectionFactory.Create(path);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT CorrelationId, SupersededByCorrelationId,
                       SupersededUtc
                FROM VeteransClaims_ReviewedMedicalLiteratureClassifications
                ORDER BY PromotedUtc;
                """;

            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(original.CorrelationId, reader.GetString(0));
            Assert.Equal(replacement.CorrelationId, reader.GetString(1));
            Assert.True(DateTimeOffset.TryParse(reader.GetString(2), out _));
            Assert.True(await reader.ReadAsync());
            Assert.Equal(replacement.CorrelationId, reader.GetString(0));
            Assert.True(reader.IsDBNull(1));
            Assert.True(reader.IsDBNull(2));
            Assert.False(await reader.ReadAsync());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReviewedClassifications_SupersessionRejectsMismatchedDecisionSetWithoutChangingActiveReview()
    {
        var path = Path.GetTempFileName();

        try
        {
            var regulatory = new SqliteRegulatoryRepository(path);
            var literature = new SqliteMedicalLiteratureRepository(path);
            var evidence = new SqliteEvidenceRepository(path);
            await literature.InitializeAsync();
            await evidence.InitializeAsync();

            var authority = new RegulatoryAuthority
            {
                Id = new("authority-supersede-mismatch"),
                AuthorityType = "Regulation",
                Citation = "38 CFR",
                Title = "Veterans Affairs"
            };
            await regulatory.AddRegulatoryAuthorityAsync(authority);
            var provision = new RegulatoryProvision
            {
                Id = new("provision-supersede-mismatch"),
                RegulatoryAuthorityId = authority.Id,
                ProvisionType = RegulatoryProvisionTypes.Requirement,
                Citation = "38 CFR 3.310"
            };
            await regulatory.AddRegulatoryProvisionAsync(provision);
            var requirement = new Requirement
            {
                Id = new("requirement-supersede-mismatch"),
                RegulatoryProvisionId = provision.Id,
                Description = "Secondary service connection requirement."
            };
            await regulatory.AddRequirementAsync(requirement);

            var source = new MedicalLiteratureSource
            {
                Id = new("study-supersede-mismatch"),
                Title = "Mismatch study",
                Authors = "Example Authors",
                Publication = "Example Journal",
                PeerReviewed = true
            };
            await literature.AddMedicalLiteratureSourceAsync(source);
            var artifact = new Artifact
            {
                Id = new("artifact-supersede-mismatch"),
                Name = "mismatch-study.pdf",
                ArtifactType = "pdf"
            };
            await evidence.AddArtifactAsync(artifact);
            await literature.AddMedicalLiteratureSourceArtifactAsync(
                new MedicalLiteratureSourceArtifact
                {
                    MedicalLiteratureSourceId = source.Id,
                    ArtifactId = artifact.Id
                });

            var reviewedUtc =
                new DateTimeOffset(2026, 9, 12, 18, 0, 0, TimeSpan.Zero);
            var original = new ReviewedMedicalLiteratureClassification
            {
                Association = new RequirementMedicalLiterature
                {
                    RequirementId = requirement.Id,
                    MedicalLiteratureSourceId = source.Id,
                    GuidanceRole = EvidenceGuidanceRoles.SupportsRequirement,
                    Description = "Initial accepted relevance."
                },
                ArtifactId = artifact.Id,
                PromotedBy = "supersession-test",
                PromotedUtc = reviewedUtc.AddMinutes(1),
                ReviewedBy = "reviewer@example.test",
                ReviewedUtc = reviewedUtc,
                IntelligenceOutput = "{}",
                CapabilityId = "TextStructuredExtraction",
                ProviderId = "test-provider",
                CorrelationId = "active-mismatch-correlation",
                EngineName = "test-engine",
                StartedUtc = reviewedUtc.AddMinutes(-2),
                CompletedUtc = reviewedUtc.AddMinutes(-1),
                RequiresReview = true,
                Warnings = [],
                SourceExcerpts =
                [
                    new MedicalLiteratureSourceExcerpt
                    {
                        ArtifactId = artifact.Id,
                        Text = "Initial excerpt.",
                        StartOffset = 0,
                        Length = 16
                    }
                ]
            };
            await literature.AddReviewedClassificationAsync(original);

            var replacement = new ReviewedMedicalLiteratureClassification
            {
                Association = new RequirementMedicalLiterature
                {
                    RequirementId = requirement.Id,
                    MedicalLiteratureSourceId = source.Id,
                    GuidanceRole = EvidenceGuidanceRoles.Corroborates,
                    Description = "Different logical decision."
                },
                ArtifactId = artifact.Id,
                PromotedBy = "supersession-test",
                PromotedUtc = reviewedUtc.AddHours(1),
                ReviewedBy = "replacement@example.test",
                ReviewedUtc = reviewedUtc.AddMinutes(59),
                IntelligenceOutput = "{}",
                CapabilityId = "TextStructuredExtraction",
                ProviderId = "test-provider",
                CorrelationId = "replacement-mismatch-correlation",
                EngineName = "test-engine",
                StartedUtc = reviewedUtc.AddMinutes(57),
                CompletedUtc = reviewedUtc.AddMinutes(58),
                RequiresReview = true,
                Warnings = [],
                SourceExcerpts =
                [
                    new MedicalLiteratureSourceExcerpt
                    {
                        ArtifactId = artifact.Id,
                        Text = "Replacement excerpt.",
                        StartOffset = 0,
                        Length = 20
                    }
                ]
            };

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => literature.SupersedeReviewedClassificationsAsync(
                    original.CorrelationId,
                    [replacement]));

            var active = Assert.Single(
                await literature.GetReviewedClassificationsAsync(
                    requirement.Id));
            Assert.Equal(original.CorrelationId, active.CorrelationId);
            Assert.Equal(
                original.Association.Description,
                active.Association.Description);
        }
        finally
        {
            File.Delete(path);
        }
    }

}
