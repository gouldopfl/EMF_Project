using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Extensions.VeteransClaims.Regulatory;

namespace EMF.Tests;

public sealed class VeteransClaimsSqliteEvidenceDevelopmentPlanRepositoryTests
{
    [Fact]
    public async Task Repository_RoundTripsEvidenceDevelopmentPlan()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteEvidenceDevelopmentPlanRepository(
                    databasePath);

            await repository.InitializeAsync();

            var veteran = new Veteran
            {
                Id = new VeteranId("veteran-001")
            };

            await new SqliteVeteranRepository(databasePath)
                .AddVeteranAsync(veteran);

            var claim = new Claim
            {
                Id = new ClaimId("claim-001"),
                VeteranId = veteran.Id
            };

            await new SqliteClaimRepository(databasePath)
                .AddClaimAsync(claim);

            var issue = new ClaimIssue
            {
                Id = new ClaimIssueId("issue-001"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

            await new SqliteClaimIssueRepository(databasePath)
                .AddClaimIssueAsync(issue);

            var plan = new EvidenceDevelopmentPlan
            {
                Id = new EvidenceDevelopmentPlanId("plan-001"),
                ClaimIssueId = issue.Id,
                Description = "Develop missing evidence."
            };

            await repository.AddEvidenceDevelopmentPlanAsync(
                plan);

            var stored =
                await repository.GetEvidenceDevelopmentPlanAsync(
                    plan.Id);

            var byIssue =
                await repository.GetEvidenceDevelopmentPlansAsync(
                    issue.Id);

            Assert.NotNull(stored);
            Assert.Equal(plan.Id, stored!.Id);
            Assert.Equal(
                plan.ClaimIssueId,
                stored.ClaimIssueId);
            Assert.Equal(
                plan.Description,
                stored.Description);

            Assert.Equal(
                plan.Id,
                Assert.Single(byIssue).Id);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Repository_RoundTripsDevelopmentPlanRequirement()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteEvidenceDevelopmentPlanRepository(databasePath);

            await repository.InitializeAsync();

            var veteran = new Veteran
            {
                Id = new VeteranId("veteran-001")
            };

            await new SqliteVeteranRepository(databasePath)
                .AddVeteranAsync(veteran);

            var claim = new Claim
            {
                Id = new ClaimId("claim-001"),
                VeteranId = veteran.Id
            };

            await new SqliteClaimRepository(databasePath)
                .AddClaimAsync(claim);

            var issue = new ClaimIssue
            {
                Id = new ClaimIssueId("issue-001"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

            await new SqliteClaimIssueRepository(databasePath)
                .AddClaimIssueAsync(issue);

            var plan = new EvidenceDevelopmentPlan
            {
                Id = new EvidenceDevelopmentPlanId("plan-001"),
                ClaimIssueId = issue.Id,
                Description = "Develop missing evidence."
            };

            await repository.AddEvidenceDevelopmentPlanAsync(plan);

            var regulatory =
                new SqliteRegulatoryRepository(databasePath);

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
                Description = "Required element."
            };

            await regulatory.AddRequirementAsync(requirement);

            var association =
                new EvidenceDevelopmentPlanRequirement
                {
                    EvidenceDevelopmentPlanId = plan.Id,
                    RequirementId = requirement.Id
                };

            await repository
                .AddEvidenceDevelopmentPlanRequirementAsync(
                    association);

            var stored =
                await repository
                    .GetEvidenceDevelopmentPlanRequirementsAsync(
                        association.EvidenceDevelopmentPlanId);

            var result = Assert.Single(stored);

            Assert.Equal(
                association.EvidenceDevelopmentPlanId,
                result.EvidenceDevelopmentPlanId);

            Assert.Equal(
                association.RequirementId,
                result.RequirementId);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

}
