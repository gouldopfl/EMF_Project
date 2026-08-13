using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

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
}
