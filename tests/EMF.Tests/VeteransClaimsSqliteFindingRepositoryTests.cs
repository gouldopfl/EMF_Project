using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

namespace EMF.Tests;

public sealed class VeteransClaimsSqliteFindingRepositoryTests
{
    [Fact]
    public async Task Repository_RoundTripsFinding()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteFindingRepository(databasePath);

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

            var claimIssue = new ClaimIssue
            {
                Id = new ClaimIssueId("claim-issue-001"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

            await new SqliteClaimIssueRepository(databasePath)
                .AddClaimIssueAsync(claimIssue);

            var finding = new Finding
            {
                Id = new FindingId("finding-001"),
                ClaimIssueId = claimIssue.Id,
                RequirementId = null,
                Outcome = FindingOutcomes.Favorable,
                Description = "Evidence supports the finding."
            };

            await repository.AddFindingAsync(finding);

            var stored =
                await repository.GetFindingAsync(
                    finding.Id);

            var byIssue =
                await repository.GetFindingsAsync(
                    claimIssue.Id);

            Assert.NotNull(stored);
            Assert.Equal(finding.Id, stored!.Id);
            Assert.Equal(
                finding.ClaimIssueId,
                stored.ClaimIssueId);
            Assert.Null(stored.RequirementId);
            Assert.Equal(finding.Outcome, stored.Outcome);
            Assert.Equal(
                finding.Description,
                stored.Description);

            Assert.Equal(
                finding.Id,
                Assert.Single(byIssue).Id);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Repository_PreservesRequirement()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteFindingRepository(databasePath);

            await repository.InitializeAsync();

            var veteran = new Veteran
            {
                Id = new VeteranId("veteran-002")
            };

            await new SqliteVeteranRepository(databasePath)
                .AddVeteranAsync(veteran);

            var claim = new Claim
            {
                Id = new ClaimId("claim-002"),
                VeteranId = veteran.Id
            };

            await new SqliteClaimRepository(databasePath)
                .AddClaimAsync(claim);

            var claimIssue = new ClaimIssue
            {
                Id = new ClaimIssueId("claim-issue-002"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

            await new SqliteClaimIssueRepository(databasePath)
                .AddClaimIssueAsync(claimIssue);

            var authorityId =
                new RegulatoryAuthorityId("authority-001");

            var provisionId =
                new RegulatoryProvisionId("provision-001");

            var requirementId =
                new RequirementId("requirement-001");

            var regulatory =
                new SqliteRegulatoryRepository(databasePath);

            await regulatory.AddRegulatoryAuthorityAsync(
                new EMF.Extensions.VeteransClaims.Regulatory.RegulatoryAuthority
                {
                    Id = authorityId,
                    AuthorityType = "Regulation",
                    Citation = "38 CFR",
                    Title = "Test Authority"
                });

            await regulatory.AddRegulatoryProvisionAsync(
                new EMF.Extensions.VeteransClaims.Regulatory.RegulatoryProvision
                {
                    Id = provisionId,
                    RegulatoryAuthorityId = authorityId,
                    ProvisionType = "Requirement",
                    Citation = "38 CFR Test"
                });

            await regulatory.AddRequirementAsync(
                new EMF.Extensions.VeteransClaims.Regulatory.Requirement
                {
                    Id = requirementId,
                    RegulatoryProvisionId = provisionId,
                    Description = "Test requirement"
                });

            var finding = new Finding
            {
                Id = new FindingId("finding-002"),
                ClaimIssueId = claimIssue.Id,
                RequirementId = requirementId,
                Outcome = FindingOutcomes.Unfavorable,
                Description = "Requirement was not satisfied."
            };

            await repository.AddFindingAsync(finding);

            var stored =
                await repository.GetFindingAsync(
                    finding.Id);

            Assert.NotNull(stored);
            Assert.Equal(
                requirementId,
                stored!.RequirementId);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}
