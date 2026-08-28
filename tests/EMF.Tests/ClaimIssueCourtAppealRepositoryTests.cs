using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

namespace EMF.Tests;

public sealed class ClaimIssueCourtAppealRepositoryTests
{
    [Fact]
    public async Task AddAsync_round_trips_court_appeal()
    {
        var path =
            Path.Combine(
                Path.GetTempPath(),
                $"{Guid.NewGuid():N}.db");

        try
        {
            await new VeteransClaimsSqliteSchema(path)
                .InitializeAsync();

            var repository =
                new SqliteClaimIssueCourtAppealRepository(path);

            var appeal =
                new ClaimIssueCourtAppeal
                {
                    ClaimIssueId =
                        new ClaimIssueId("issue-001"),
                    Court = "CAVC",
                    FiledAt =
                        new DateTimeOffset(
                            2026, 8, 15, 0, 0, 0,
                            TimeSpan.Zero),
                    DocketNumber = "26-1234",
                    Outcome = "Remanded",
                    DecidedAt =
                        new DateTimeOffset(
                            2027, 2, 1, 0, 0, 0,
                            TimeSpan.Zero)
                };

            await repository.AddAsync(appeal);

            var results =
                await repository.GetByClaimIssueAsync(
                    appeal.ClaimIssueId);

            var result = Assert.Single(results);

            Assert.Equal("CAVC", result.Court);
            Assert.Equal("26-1234", result.DocketNumber);
            Assert.Equal("Remanded", result.Outcome);
            Assert.Equal(appeal.FiledAt, result.FiledAt);
            Assert.Equal(appeal.DecidedAt, result.DecidedAt);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
