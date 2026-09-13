using EMF.ConsoleApplication;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Medications;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

namespace EMF.Tests;

public sealed partial class VeteransConsoleCommandTests
{
    [Fact]
    public async Task EvidenceMedicationHistory_PersistsHistoryEvent()
    {
        var path = Path.GetTempFileName();

        try
        {
            await SeedMedicationTestDatabaseAsync(path);

            using var output = new StringWriter();

            var exitCode =
                await VeteransConsoleCommand.RunEvidenceMedicationHistoryAsync(
                    path,
                    new VeteranId("veteran-med-001"),
                    new ArtifactId("blue-button-med-001"),
                    new DateOnly(2022, 12, 29),
                    2448,
                    MedicationHistoryEventTypes.LastReleased,
                    "Trazodone HCl",
                    "100MG",
                    "TAKE TWO TABLETS ORALLY AT BEDTIME FOR SLEEP",
                    "FOR SLEEP",
                    "10608899D",
                    output);

            Assert.Equal(0, exitCode);

            var repository =
                new SqliteMedicationRepository(path);

            var events =
                await repository.GetMedicationHistoryEventsAsync(
                    new VeteranId("veteran-med-001"));

            var stored = Assert.Single(events);

            Assert.Equal("Trazodone HCl", stored.MedicationName);
            Assert.Equal(
                MedicationHistoryEventTypes.LastReleased,
                stored.EventType);
            Assert.Equal(new DateOnly(2022, 12, 29), stored.EventDate);
            Assert.Equal(2448, stored.SourcePage);
            Assert.Equal("100MG", stored.Strength);
            Assert.Equal("FOR SLEEP", stored.PharmacyIndication);
            Assert.Equal("10608899D", stored.PrescriptionNumber);
            Assert.Equal(
                new ArtifactId("blue-button-med-001"),
                stored.SourceArtifactId);

            var rendered = output.ToString();
            Assert.Contains("Medication History ID :", rendered);
            Assert.Contains("Medication            : Trazodone HCl", rendered);
            Assert.Contains("Event Type            : LastReleased", rendered);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task EvidenceMedicationHistory_RejectsMissingVeteran()
    {
        var path = Path.GetTempFileName();

        try
        {
            await SeedMedicationTestDatabaseAsync(path);

            using var output = new StringWriter();

            var exitCode =
                await VeteransConsoleCommand.RunEvidenceMedicationHistoryAsync(
                    path,
                    new VeteranId("missing-veteran"),
                    new ArtifactId("blue-button-med-001"),
                    new DateOnly(2022, 12, 29),
                    2448,
                    MedicationHistoryEventTypes.LastReleased,
                    "Trazodone HCl",
                    null,
                    null,
                    null,
                    null,
                    output);

            Assert.Equal(2, exitCode);

            var repository =
                new SqliteMedicationRepository(path);

            var events =
                await repository.GetMedicationHistoryEventsAsync(
                    new VeteranId("veteran-med-001"));

            Assert.Empty(events);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task EvidenceMedicationHistory_RejectsMissingSourceArtifact()
    {
        var path = Path.GetTempFileName();

        try
        {
            await SeedMedicationTestDatabaseAsync(path);

            using var output = new StringWriter();

            var exitCode =
                await VeteransConsoleCommand.RunEvidenceMedicationHistoryAsync(
                    path,
                    new VeteranId("veteran-med-001"),
                    new ArtifactId("missing-artifact"),
                    new DateOnly(2022, 12, 29),
                    2448,
                    MedicationHistoryEventTypes.LastReleased,
                    "Trazodone HCl",
                    null,
                    null,
                    null,
                    null,
                    output);

            Assert.Equal(2, exitCode);

            var repository =
                new SqliteMedicationRepository(path);

            var events =
                await repository.GetMedicationHistoryEventsAsync(
                    new VeteranId("veteran-med-001"));

            Assert.Empty(events);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task EvidenceMedicationHistory_RoutesThroughRunAsync()
    {
        var path = Path.GetTempFileName();

        try
        {
            await SeedMedicationTestDatabaseAsync(path);

            var exitCode =
                await VeteransConsoleCommand.RunAsync(
                    [
                        "evidence",
                        "medication",
                        "history",
                        path,
                        "veteran-med-001",
                        "blue-button-med-001",
                        "2022-10-21",
                        "2442",
                        "LastReleased",
                        "Bupropion HCl",
                        "150MG 24HR SA",
                        "TAKE THREE TABLETS ORALLY EVERY DAY",
                        "-",
                        "11284255"
                    ]);

            Assert.Equal(0, exitCode);

            var repository =
                new SqliteMedicationRepository(path);

            var events =
                await repository.GetMedicationHistoryEventsAsync(
                    new VeteranId("veteran-med-001"),
                    "bupropion hcl");

            var stored = Assert.Single(events);
            Assert.Equal(new DateOnly(2022, 10, 21), stored.EventDate);
            Assert.Equal(2442, stored.SourcePage);
            Assert.Equal("11284255", stored.PrescriptionNumber);
            Assert.Null(stored.PharmacyIndication);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
