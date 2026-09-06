using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

namespace EMF.Tests;

public sealed class
    VeteransClaimsSqliteClaimIssueExposureRepositoryTests
{
    [Fact]
    public async Task Repository_RoundTripsClaimIssueExposure()
    {
        var databasePath =
            Path.Combine(
                Path.GetTempPath(),
                $"{Guid.NewGuid():N}.db");

        try
        {
            var veteran =
                new Veteran
                {
                    Id = new VeteranId("veteran-001")
                };

            var veteranRepository =
                new SqliteVeteranRepository(databasePath);
            await veteranRepository.InitializeAsync();
            await veteranRepository.AddVeteranAsync(veteran);

            var claim =
                new Claim
                {
                    Id = new ClaimId("claim-001"),
                    VeteranId = veteran.Id
                };
            var claimRepository =
                new SqliteClaimRepository(databasePath);
            await claimRepository.AddClaimAsync(claim);

            var claimIssue =
                new ClaimIssue
                {
                    Id = new ClaimIssueId("claim-issue-001"),
                    ClaimId = claim.Id,
                    ClaimIssueType = "ServiceConnection"
                };
            var claimIssueRepository =
                new SqliteClaimIssueRepository(databasePath);
            await claimIssueRepository.AddClaimIssueAsync(claimIssue);

            var exposure =
                new Exposure
                {
                    Id = new ExposureId("exposure-001"),
                    VeteranId = veteran.Id,
                    ExposureType = "Environmental"
                };
            var repository =
                new SqliteServiceHistoryRepository(databasePath);
            await repository.AddExposureAsync(exposure);

            await repository.AddClaimIssueExposureAsync(
                new ClaimIssueExposure
                {
                    ClaimIssueId = claimIssue.Id,
                    ExposureId = exposure.Id
                });

            Assert.Equal(
                exposure.Id,
                Assert.Single(
                    await repository.GetExposureIdsAsync(
                        claimIssue.Id)));

            Assert.Equal(
                claimIssue.Id,
                Assert.Single(
                    await repository.GetClaimIssueIdsAsync(
                        exposure.Id)));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Repository_RejectsCrossVeteranAssociation()
    {
        var databasePath =
            Path.Combine(
                Path.GetTempPath(),
                $"{Guid.NewGuid():N}.db");

        try
        {
            var firstVeteran =
                new Veteran
                {
                    Id = new VeteranId("veteran-001")
                };
            var secondVeteran =
                new Veteran
                {
                    Id = new VeteranId("veteran-002")
                };

            var veteranRepository =
                new SqliteVeteranRepository(databasePath);
            await veteranRepository.InitializeAsync();
            await veteranRepository.AddVeteranAsync(firstVeteran);
            await veteranRepository.AddVeteranAsync(secondVeteran);

            var claim =
                new Claim
                {
                    Id = new ClaimId("claim-001"),
                    VeteranId = firstVeteran.Id
                };
            var claimRepository =
                new SqliteClaimRepository(databasePath);
            await claimRepository.AddClaimAsync(claim);

            var claimIssue =
                new ClaimIssue
                {
                    Id = new ClaimIssueId("claim-issue-001"),
                    ClaimId = claim.Id,
                    ClaimIssueType = "ServiceConnection"
                };
            var claimIssueRepository =
                new SqliteClaimIssueRepository(databasePath);
            await claimIssueRepository.AddClaimIssueAsync(claimIssue);

            var exposure =
                new Exposure
                {
                    Id = new ExposureId("exposure-001"),
                    VeteranId = secondVeteran.Id,
                    ExposureType = "Environmental"
                };
            var repository =
                new SqliteServiceHistoryRepository(databasePath);
            await repository.AddExposureAsync(exposure);

            var exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => repository.AddClaimIssueExposureAsync(
                        new ClaimIssueExposure
                        {
                            ClaimIssueId = claimIssue.Id,
                            ExposureId = exposure.Id
                        }));

            Assert.Contains(
                "belong to the same veteran",
                exception.Message);
            Assert.Empty(
                await repository.GetExposureIdsAsync(
                    claimIssue.Id));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}
