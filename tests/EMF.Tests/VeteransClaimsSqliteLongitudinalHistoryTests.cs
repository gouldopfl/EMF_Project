using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

namespace EMF.Tests;

public sealed class VeteransClaimsSqliteLongitudinalHistoryTests
{
    [Fact]
    public async Task ClaimIssue_PersistsAcrossSubmissionHistory()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var schema =
                new VeteransClaimsSqliteSchema(databasePath);

            await schema.InitializeAsync();

            var veteran = new Veteran
            {
                Id = new VeteranId("veteran-001")
            };

            var veteranRepository =
                new SqliteVeteranRepository(databasePath);

            await veteranRepository.AddVeteranAsync(
                veteran);

            var claim = new Claim
            {
                Id = new ClaimId("claim-001"),
                VeteranId = veteran.Id
            };

            var claimRepository =
                new SqliteClaimRepository(databasePath);

            await claimRepository.AddClaimAsync(claim);

            var claimIssue = new ClaimIssue
            {
                Id =
                    new ClaimIssueId("claim-issue-001"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

            var claimIssueRepository =
                new SqliteClaimIssueRepository(databasePath);

            await claimIssueRepository.AddClaimIssueAsync(
                claimIssue);

            var initialSubmission = new Submission
            {
                Id =
                    new SubmissionId("submission-001"),
                ClaimId = claim.Id,
                SubmissionType =
                    SubmissionTypes.InitialClaim
            };

            var supplementalSubmission = new Submission
            {
                Id =
                    new SubmissionId("submission-002"),
                ClaimId = claim.Id,
                SubmissionType =
                    SubmissionTypes.SupplementalClaim
            };

            var submissionRepository =
                new SqliteSubmissionRepository(
                    databasePath);

            await submissionRepository.AddSubmissionAsync(
                initialSubmission,
                new[] { claimIssue.Id });

            await submissionRepository.AddSubmissionAsync(
                supplementalSubmission,
                new[] { claimIssue.Id });

            var submissionHistory =
                await submissionRepository
                    .GetSubmissionsAsync(claim.Id);

            var initialIssueIds =
                await submissionRepository
                    .GetClaimIssueIdsAsync(
                        initialSubmission.Id);

            var supplementalIssueIds =
                await submissionRepository
                    .GetClaimIssueIdsAsync(
                        supplementalSubmission.Id);

            Assert.Equal(2, submissionHistory.Count);

            Assert.Contains(
                submissionHistory,
                item =>
                    item.SubmissionType ==
                    SubmissionTypes.InitialClaim);

            Assert.Contains(
                submissionHistory,
                item =>
                    item.SubmissionType ==
                    SubmissionTypes.SupplementalClaim);

            Assert.Equal(
                claimIssue.Id,
                Assert.Single(initialIssueIds));

            Assert.Equal(
                claimIssue.Id,
                Assert.Single(supplementalIssueIds));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}
