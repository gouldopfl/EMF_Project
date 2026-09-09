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
}
