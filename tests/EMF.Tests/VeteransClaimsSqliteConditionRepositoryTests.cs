using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Conditions;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class VeteransClaimsSqliteConditionRepositoryTests
{
    [Fact]
    public async Task Repository_RoundTripsClaimedConditions()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(
                databasePath)
                .InitializeAsync();

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

            var claimIssue = new ClaimIssue
            {
                Id =
                    new ClaimIssueId(
                        "claim-issue-001"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

            await new SqliteClaimIssueRepository(
                databasePath)
                .AddClaimIssueAsync(claimIssue);

            var condition = new ClaimedCondition
            {
                Id =
                    new ClaimedConditionId(
                        "claimed-condition-001"),
                ClaimIssueId = claimIssue.Id,
                Name = "Sleep apnea"
            };

            var repository =
                new SqliteConditionRepository(
                    databasePath);

            await repository.AddClaimedConditionAsync(
                condition);

            var stored =
                await repository
                    .GetClaimedConditionAsync(
                        condition.Id);

            var issueConditions =
                await repository
                    .GetClaimedConditionsAsync(
                        claimIssue.Id);

            Assert.NotNull(stored);
            Assert.Equal(condition.Id, stored!.Id);
            Assert.Equal(
                condition.ClaimIssueId,
                stored.ClaimIssueId);
            Assert.Equal(condition.Name, stored.Name);

            Assert.Equal(
                condition.Id,
                Assert.Single(issueConditions).Id);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
    [Fact]
    public async Task
        Repository_RejectsConditionForMissingClaimIssue()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteConditionRepository(
                    databasePath);

            await repository.InitializeAsync();

            var condition = new ClaimedCondition
            {
                Id =
                    new ClaimedConditionId(
                        "claimed-condition-001"),
                ClaimIssueId =
                    new ClaimIssueId(
                        "missing-claim-issue"),
                Name = "Sleep apnea"
            };

            await Assert.ThrowsAsync<SqliteException>(
                () => repository
                    .AddClaimedConditionAsync(
                        condition));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

}
