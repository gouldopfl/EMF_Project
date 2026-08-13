using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

namespace EMF.Tests;

public sealed class VeteransClaimsSqliteMedicalOpinionRepositoryTests
{
    [Fact]
    public async Task Repository_RoundTripsMedicalOpinion()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteMedicalOpinionRepository(databasePath);

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

            var opinion = new MedicalOpinion
            {
                Id = new MedicalOpinionId("opinion-001"),
                ClaimIssueId = claimIssue.Id,
                Question = "Is the condition related to service?",
                Opinion = "At least as likely as not."
            };

            await repository.AddMedicalOpinionAsync(opinion);

            var stored =
                await repository.GetMedicalOpinionAsync(opinion.Id);

            var byIssue =
                await repository.GetMedicalOpinionsAsync(claimIssue.Id);

            Assert.NotNull(stored);
            Assert.Equal(opinion.Id, stored!.Id);
            Assert.Equal(opinion.ClaimIssueId, stored.ClaimIssueId);
            Assert.Equal(opinion.Question, stored.Question);
            Assert.Equal(opinion.Opinion, stored.Opinion);
            Assert.Equal(opinion.Id, Assert.Single(byIssue).Id);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}
