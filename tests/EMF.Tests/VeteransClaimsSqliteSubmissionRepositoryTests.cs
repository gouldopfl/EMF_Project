using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

namespace EMF.Tests;

public sealed class VeteransClaimsSqliteSubmissionRepositoryTests
{
    [Fact]
    public async Task Repository_PersistsSubmissionAndIssueAssociations()
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

            var claimIssueRepository =
                new SqliteClaimIssueRepository(databasePath);

            var firstIssue = new ClaimIssue
            {
                Id =
                    new ClaimIssueId("claim-issue-001"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

            var secondIssue = new ClaimIssue
            {
                Id =
                    new ClaimIssueId("claim-issue-002"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.IncreasedEvaluation
            };

            await claimIssueRepository.AddClaimIssueAsync(
                firstIssue);

            await claimIssueRepository.AddClaimIssueAsync(
                secondIssue);

            ISubmissionRepository repository =
                new SqliteSubmissionRepository(databasePath);

            var submission = new Submission
            {
                Id = new SubmissionId("submission-001"),
                ClaimId = claim.Id,
                SubmissionType =
                    SubmissionTypes.InitialClaim,
                SubmittedAt =
                    new DateTimeOffset(
                        2026, 1, 10, 14, 30, 0, TimeSpan.Zero),
                ReceivedAt =
                    new DateTimeOffset(
                        2026, 1, 10, 15, 5, 0, TimeSpan.Zero)
            };

            await repository.AddSubmissionAsync(
                submission,
                new[]
                {
                    firstIssue.Id,
                    secondIssue.Id
                });

            var stored =
                await repository.GetSubmissionAsync(
                    submission.Id);

            var claimSubmissions =
                await repository.GetSubmissionsAsync(
                    claim.Id);

            var issueIds =
                await repository.GetClaimIssueIdsAsync(
                    submission.Id);

            Assert.NotNull(stored);
            Assert.Equal(submission.Id, stored!.Id);
            Assert.Equal(
                SubmissionTypes.InitialClaim,
                stored.SubmissionType);
            Assert.Equal(
                submission.SubmittedAt,
                stored.SubmittedAt);
            Assert.Equal(
                submission.ReceivedAt,
                stored.ReceivedAt);

            Assert.Single(claimSubmissions);
            Assert.Equal(2, issueIds.Count);
            Assert.Contains(firstIssue.Id, issueIds);
            Assert.Contains(secondIssue.Id, issueIds);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Repository_RejectsSubmissionWithoutIssues()
    {
        var repository =
            new SqliteSubmissionRepository(
                Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));

        var submission = new Submission
        {
            Id = new SubmissionId("submission-001"),
            ClaimId = new ClaimId("claim-001"),
            SubmissionType =
                SubmissionTypes.InitialClaim
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => repository.AddSubmissionAsync(
                submission,
                Array.Empty<ClaimIssueId>()));
    }

    [Fact]
    public async Task Repository_RejectsIssueFromDifferentClaim()
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

            var firstClaim = new Claim
            {
                Id = new ClaimId("claim-001"),
                VeteranId = veteran.Id
            };

            var secondClaim = new Claim
            {
                Id = new ClaimId("claim-002"),
                VeteranId = veteran.Id
            };

            var claimRepository =
                new SqliteClaimRepository(databasePath);

            await claimRepository.AddClaimAsync(
                firstClaim);

            await claimRepository.AddClaimAsync(
                secondClaim);

            var claimIssue = new ClaimIssue
            {
                Id =
                    new ClaimIssueId("claim-issue-001"),
                ClaimId = secondClaim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

            var claimIssueRepository =
                new SqliteClaimIssueRepository(
                    databasePath);

            await claimIssueRepository.AddClaimIssueAsync(
                claimIssue);

            ISubmissionRepository repository =
                new SqliteSubmissionRepository(
                    databasePath);

            var submission = new Submission
            {
                Id =
                    new SubmissionId("submission-001"),
                ClaimId = firstClaim.Id,
                SubmissionType =
                    SubmissionTypes.InitialClaim
            };

            await Assert.ThrowsAsync<
                InvalidOperationException>(
                    () => repository.AddSubmissionAsync(
                        submission,
                        new[] { claimIssue.Id }));

            var stored =
                await repository.GetSubmissionAsync(
                    submission.Id);

            Assert.Null(stored);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}
