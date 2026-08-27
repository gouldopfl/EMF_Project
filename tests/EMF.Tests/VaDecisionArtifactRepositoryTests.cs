using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

namespace EMF.Tests;

public sealed class VaDecisionArtifactRepositoryTests
{
    [Fact]
    public async Task Repository_RoundTripsArtifact()
    {
        var path = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(path)
                .InitializeAsync();

            var veteran =
                new Veteran
                {
                    Id = new VeteranId("v1")
                };

            await new SqliteVeteranRepository(path)
                .AddVeteranAsync(veteran);

            var claim =
                new Claim
                {
                    Id = new ClaimId("c1"),
                    VeteranId = veteran.Id
                };

            await new SqliteClaimRepository(path)
                .AddClaimAsync(claim);

            var issue =
                new ClaimIssue
                {
                    Id = new ClaimIssueId("i1"),
                    ClaimId = claim.Id,
                    ClaimIssueType =
                        ClaimIssueTypes.ServiceConnection
                };

            await new SqliteClaimIssueRepository(path)
                .AddClaimIssueAsync(issue);

            var decision =
                new VaDecision
                {
                    Id = new VaDecisionId("d1"),
                    DecisionDate = DateTimeOffset.UtcNow
                };

            IVaDecisionRepository repository =
                new SqliteVaDecisionRepository(path);

            await repository.AddDecisionAsync(
                decision,
                [
                    new IssueDecision
                    {
                        Id = new IssueDecisionId("id1"),
                        VaDecisionId = decision.Id,
                        ClaimIssueId = issue.Id,
                        Outcome = IssueDecisionOutcomes.Denied
                    }
                ],
                []);

            var artifactId =
                new ArtifactId("artifact-1");

            await repository.AddDecisionArtifactAsync(
                new VaDecisionArtifact
                {
                    VaDecisionId = decision.Id,
                    ArtifactId = artifactId
                });

            var stored =
                await repository.GetArtifactIdsAsync(
                    decision.Id);

            Assert.Equal(
                new[] { artifactId },
                stored);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
