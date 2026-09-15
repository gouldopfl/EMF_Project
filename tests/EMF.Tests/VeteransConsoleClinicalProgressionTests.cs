using EMF.ConsoleApplication;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Clinical;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Persistence.Repositories;

namespace EMF.Tests;

public sealed partial class VeteransConsoleCommandTests
{
    [Fact]
    public async Task EvidenceClinicalProgression_PersistsAndIsIdempotent()
    {
        var path = Path.GetTempFileName();

        try
        {
            await SeedClinicalProgressionDatabaseAsync(path);

            using var first = new StringWriter();
            using var second = new StringWriter();

            var args = new object?[]
            {
                path,
                new ClaimIssueId("issue-osa"),
                new ArtifactId("clinical-note-001"),
                new DateOnly(2021, 11, 15),
                null,
                null,
                ClinicalProgressionEventTypes.TreatmentProblem,
                "Sleep Med PAP Clinic Note",
                "High mask leak was documented despite 100% PAP compliance."
            };

            Assert.Equal(
                0,
                await VeteransConsoleCommand.RunEvidenceClinicalProgressionAsync(
                    (string)args[0]!,
                    (ClaimIssueId)args[1]!,
                    (ArtifactId)args[2]!,
                    (DateOnly)args[3]!,
                    (int?)args[4],
                    (int?)args[5],
                    (string)args[6]!,
                    (string)args[7]!,
                    (string)args[8]!,
                    first));

            Assert.Equal(
                0,
                await VeteransConsoleCommand.RunEvidenceClinicalProgressionAsync(
                    (string)args[0]!,
                    (ClaimIssueId)args[1]!,
                    (ArtifactId)args[2]!,
                    (DateOnly)args[3]!,
                    (int?)args[4],
                    (int?)args[5],
                    (string)args[6]!,
                    (string)args[7]!,
                    (string)args[8]!,
                    second));

            var stored =
                await new SqliteClinicalProgressionRepository(path)
                    .GetAsync(new ClaimIssueId("issue-osa"));

            Assert.Single(stored);
            Assert.Contains("Already Persisted     : False", first.ToString());
            Assert.Contains("Already Persisted     : True", second.ToString());
            Assert.Contains("Internal Source Pages : None", first.ToString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task EvidenceClinicalProgression_RoutesThroughRunAsync()
    {
        var path = Path.GetTempFileName();

        try
        {
            await SeedClinicalProgressionDatabaseAsync(path);

            var exitCode =
                await VeteransConsoleCommand.RunAsync(
                    [
                        "evidence",
                        "clinical",
                        "progression",
                        path,
                        "issue-osa",
                        "clinical-note-001",
                        "2021-11-15",
                        "-",
                        "-",
                        ClinicalProgressionEventTypes.TreatmentProblem,
                        "Sleep Med PAP Clinic Note",
                        "High mask leak was documented despite 100% PAP compliance."
                    ]);

            Assert.Equal(0, exitCode);

            Assert.Single(
                await new SqliteClinicalProgressionRepository(path)
                    .GetAsync(new ClaimIssueId("issue-osa")));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task EvidenceClinicalProgression_RejectsUnsupportedEventType()
    {
        var path = Path.GetTempFileName();

        try
        {
            await SeedClinicalProgressionDatabaseAsync(path);
            using var output = new StringWriter();

            var exitCode =
                await VeteransConsoleCommand.RunEvidenceClinicalProgressionAsync(
                    path,
                    new ClaimIssueId("issue-osa"),
                    new ArtifactId("clinical-note-001"),
                    new DateOnly(2021, 11, 15),
                    null,
                    null,
                    "Guess",
                    "Sleep Medicine Note",
                    "Do not infer an event type.",
                    output);

            Assert.Equal(2, exitCode);
            Assert.Empty(
                await new SqliteClinicalProgressionRepository(path)
                    .GetAsync(new ClaimIssueId("issue-osa")));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task SeedClinicalProgressionDatabaseAsync(
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
                Id = new ArtifactId("clinical-note-001"),
                Name = "Sleep Medicine Note",
                ArtifactType = "veterans-clinical-note"
            });
    }
}
