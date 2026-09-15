using EMF.ConsoleApplication;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Persistence.Repositories;

namespace EMF.Tests;

public sealed partial class VeteransConsoleCommandTests
{
    [Fact]
    public async Task EvidenceClarification_PersistsAndIsIdempotent()
    {
        var path = Path.GetTempFileName();

        try
        {
            await SeedSourceClarificationDatabaseAsync(path);

            using var first = new StringWriter();
            using var second = new StringWriter();

            var args = new object[]
            {
                path,
                new ClaimIssueId("issue-osa"),
                new ArtifactId("blue-button-001"),
                new DateOnly(2024, 1, 25),
                1600,
                1600,
                SourceClarificationCategories.ImpossibleMagnitude,
                "Sleep Medicine Note",
                "8200 pounds",
                "The source record states ‘8200 pounds.’ The Veteran reports the intended value is 82 pounds. Original source text is preserved."
            };

            Assert.Equal(
                0,
                await VeteransConsoleCommand.RunEvidenceSourceClarificationAsync(
                    (string)args[0],
                    (ClaimIssueId)args[1],
                    (ArtifactId)args[2],
                    (DateOnly)args[3],
                    (int)args[4],
                    (int)args[5],
                    (string)args[6],
                    (string)args[7],
                    (string)args[8],
                    (string)args[9],
                    first));

            Assert.Equal(
                0,
                await VeteransConsoleCommand.RunEvidenceSourceClarificationAsync(
                    (string)args[0],
                    (ClaimIssueId)args[1],
                    (ArtifactId)args[2],
                    (DateOnly)args[3],
                    (int)args[4],
                    (int)args[5],
                    (string)args[6],
                    (string)args[7],
                    (string)args[8],
                    (string)args[9],
                    second));

            var stored =
                await new SqliteSourceClarificationRepository(path)
                    .GetAsync(new ClaimIssueId("issue-osa"));

            Assert.Single(stored);
            Assert.Contains("Already Persisted     : False", first.ToString());
            Assert.Contains("Already Persisted     : True", second.ToString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task EvidenceClarification_RoutesThroughRunAsync()
    {
        var path = Path.GetTempFileName();

        try
        {
            await SeedSourceClarificationDatabaseAsync(path);

            var exitCode =
                await VeteransConsoleCommand.RunAsync(
                    [
                        "evidence",
                        "clarification",
                        path,
                        "issue-osa",
                        "blue-button-001",
                        "2024-01-25",
                        "1600",
                        "1600",
                        SourceClarificationCategories.ImpossibleMagnitude,
                        "Sleep Medicine Note",
                        "8200 pounds",
                        "Source clarification."
                    ]);

            Assert.Equal(0, exitCode);

            Assert.Single(
                await new SqliteSourceClarificationRepository(path)
                    .GetAsync(new ClaimIssueId("issue-osa")));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task EvidenceClarification_RejectsUnsupportedCategory()
    {
        var path = Path.GetTempFileName();

        try
        {
            await SeedSourceClarificationDatabaseAsync(path);
            using var output = new StringWriter();

            var exitCode =
                await VeteransConsoleCommand.RunEvidenceSourceClarificationAsync(
                    path,
                    new ClaimIssueId("issue-osa"),
                    new ArtifactId("blue-button-001"),
                    new DateOnly(2024, 1, 25),
                    1600,
                    1600,
                    "Guess",
                    "Sleep Medicine Note",
                    "8200 pounds",
                    "Do not infer corrections.",
                    output);

            Assert.Equal(2, exitCode);
            Assert.Empty(
                await new SqliteSourceClarificationRepository(path)
                    .GetAsync(new ClaimIssueId("issue-osa")));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task SeedSourceClarificationDatabaseAsync(
        string path)
    {
        await new VeteransClaimsSqliteSchema(path)
            .InitializeAsync();

        var veteran =
            new Veteran
            {
                Id = new VeteranId("veteran-osa")
            };

        await new SqliteVeteranRepository(path)
            .AddVeteranAsync(veteran);

        var claim =
            new Claim
            {
                Id = new ClaimId("claim-osa"),
                VeteranId = veteran.Id
            };

        await new SqliteClaimRepository(path)
            .AddClaimAsync(claim);

        await new SqliteClaimIssueRepository(path)
            .AddClaimIssueAsync(
                new ClaimIssue
                {
                    Id = new ClaimIssueId("issue-osa"),
                    ClaimId = claim.Id,
                    ClaimIssueType = ClaimIssueTypes.ServiceConnection
                });

        var evidence = new SqliteEvidenceRepository(path);
        await evidence.InitializeAsync();
        await evidence.AddArtifactAsync(
            new Artifact
            {
                Id = new ArtifactId("blue-button-001"),
                Name = "VA Blue Button Report",
                ArtifactType = "Test"
            });
    }
}
