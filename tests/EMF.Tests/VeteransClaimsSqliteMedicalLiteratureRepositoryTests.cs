using EMF.Persistence.Repositories;
using EMF.Core.Models.Identities;
using EMF.Core.Models;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
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

}
