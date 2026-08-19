using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Extensions.VeteransClaims.Regulatory;

namespace EMF.Tests;

public sealed class VeteransClaimsSqliteEvidenceRequirementGuidanceRepositoryTests
{
    [Fact]
    public async Task Repository_RoundTripsGuidance()
    {
        var path = Path.GetTempFileName();

        try
        {
            var regulatory = new SqliteRegulatoryRepository(path);
            var repository =
                new SqliteEvidenceRequirementGuidanceRepository(path);

            await repository.InitializeAsync();

            var authority = new RegulatoryAuthority
            {
                Id = new RegulatoryAuthorityId("authority-001"),
                AuthorityType = "Regulation",
                Citation = "38 CFR",
                Title = "Veterans Affairs"
            };

            await regulatory.AddRegulatoryAuthorityAsync(authority);

            var provision = new RegulatoryProvision
            {
                Id = new RegulatoryProvisionId("provision-001"),
                RegulatoryAuthorityId = authority.Id,
                ProvisionType = RegulatoryProvisionTypes.Requirement,
                Citation = "38 CFR 3.303"
            };

            await regulatory.AddRegulatoryProvisionAsync(provision);

            var requirement = new Requirement
            {
                Id = new RequirementId("requirement-001"),
                RegulatoryProvisionId = provision.Id,
                Description = "Establish the required element."
            };

            await regulatory.AddRequirementAsync(requirement);

            var guidance = new EvidenceRequirementGuidance
            {
                Id = new EvidenceRequirementGuidanceId("guidance-001"),
                RequirementId = requirement.Id,
                EvidenceClassification =
                    EvidenceClassifications.MedicalOpinion,
                GuidanceRole =
                    EvidenceGuidanceRoles.SupportsRequirement,
                Description =
                    "A medical opinion may help support the requirement."
            };

            await repository.AddEvidenceRequirementGuidanceAsync(guidance);

            var stored =
                await repository.GetEvidenceRequirementGuidanceAsync(
                    guidance.Id);

            Assert.NotNull(stored);
            Assert.Equal(guidance.Id, stored!.Id);
            Assert.Equal(
                guidance.RequirementId,
                stored.RequirementId);
            Assert.Equal(
                guidance.EvidenceClassification,
                stored.EvidenceClassification);
            Assert.Equal(
                guidance.GuidanceRole,
                stored.GuidanceRole);
            Assert.Equal(
                guidance.Description,
                stored.Description);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
