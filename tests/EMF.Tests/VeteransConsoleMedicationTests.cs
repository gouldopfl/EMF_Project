using EMF.ConsoleApplication;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Persistence.Repositories;

namespace EMF.Tests;

public sealed partial class VeteransConsoleCommandTests
{
    [Fact]
    public async Task EvidenceMedication_PersistsMedicationRecord()
    {
        var path = Path.GetTempFileName();

        try
        {
            await SeedMedicationTestDatabaseAsync(path);

            using var output = new StringWriter();

            var exitCode =
                await VeteransConsoleCommand.RunEvidenceMedicationAsync(
                    path,
                    new VeteranId("veteran-med-001"),
                    new ArtifactId("blue-button-med-001"),
                    new DateOnly(2026, 9, 9),
                    429,
                    "Active",
                    "Trazodone HCl",
                    "100MG",
                    "TAKE THREE TABLETS ORALLY AT BEDTIME",
                    "FOR INSOMNIA",
                    "VA",
                    output);

            Assert.Equal(0, exitCode);

            var repository =
                new SqliteMedicationRepository(path);

            var records =
                await repository.GetMedicationRecordsAsync(
                    new VeteranId("veteran-med-001"));

            var stored = Assert.Single(records);

            Assert.Equal("Trazodone HCl", stored.MedicationName);
            Assert.Equal("100MG", stored.Strength);
            Assert.Equal("Active", stored.Status);
            Assert.Equal(429, stored.SourcePage);
            Assert.Equal(
                new ArtifactId("blue-button-med-001"),
                stored.SourceArtifactId);

            var rendered = output.ToString();
            Assert.Contains("Medication Record ID :", rendered);
            Assert.Contains("Medication           : Trazodone HCl", rendered);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task EvidenceMedication_RejectsMissingVeteran()
    {
        var path = Path.GetTempFileName();

        try
        {
            await SeedMedicationTestDatabaseAsync(path);

            using var output = new StringWriter();

            var exitCode =
                await VeteransConsoleCommand.RunEvidenceMedicationAsync(
                    path,
                    new VeteranId("missing-veteran"),
                    new ArtifactId("blue-button-med-001"),
                    new DateOnly(2026, 9, 9),
                    429,
                    "Active",
                    "Trazodone HCl",
                    null,
                    null,
                    null,
                    null,
                    output);

            Assert.Equal(2, exitCode);

            var repository =
                new SqliteMedicationRepository(path);

            var records =
                await repository.GetMedicationRecordsAsync(
                    new VeteranId("veteran-med-001"));

            Assert.Empty(records);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task EvidenceMedication_RejectsMissingSourceArtifact()
    {
        var path = Path.GetTempFileName();

        try
        {
            await SeedMedicationTestDatabaseAsync(path);

            using var output = new StringWriter();

            var exitCode =
                await VeteransConsoleCommand.RunEvidenceMedicationAsync(
                    path,
                    new VeteranId("veteran-med-001"),
                    new ArtifactId("missing-artifact"),
                    new DateOnly(2026, 9, 9),
                    429,
                    "Active",
                    "Trazodone HCl",
                    null,
                    null,
                    null,
                    null,
                    output);

            Assert.Equal(2, exitCode);

            var repository =
                new SqliteMedicationRepository(path);

            var records =
                await repository.GetMedicationRecordsAsync(
                    new VeteranId("veteran-med-001"));

            Assert.Empty(records);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task EvidenceMedication_RejectsInvalidRecordDate()
    {
        var path = Path.GetTempFileName();

        try
        {
            var exitCode =
                await VeteransConsoleCommand.RunAsync(
                    [
                        "evidence",
                        "medication",
                        path,
                        "veteran-med-001",
                        "blue-button-med-001",
                        "09/09/2026",
                        "429",
                        "Active",
                        "Trazodone HCl",
                        "100MG",
                        "-",
                        "-"
                    ]);

            Assert.Equal(2, exitCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task SeedMedicationTestDatabaseAsync(
        string path)
    {
        await new VeteransClaimsSqliteSchema(path)
            .InitializeAsync();

        await new SqliteVeteranRepository(path)
            .AddVeteranAsync(
                new Veteran
                {
                    Id = new VeteranId("veteran-med-001")
                });

        var evidence =
            new SqliteEvidenceRepository(path);

        await evidence.InitializeAsync();

        await evidence.AddArtifactAsync(
            new Artifact
            {
                Id = new ArtifactId("blue-button-med-001"),
                Name = "VA Blue Button Report",
                ArtifactType = "Test"
            });
    }
}
