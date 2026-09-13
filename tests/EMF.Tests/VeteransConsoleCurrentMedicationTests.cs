using EMF.ConsoleApplication;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Medications;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

namespace EMF.Tests;

public sealed partial class VeteransConsoleCommandTests
{
    [Fact]
    public async Task EvidenceCurrentMedications_ShowsResolvedCurrentList()
    {
        var path = Path.GetTempFileName();

        try
        {
            await SeedMedicationTestDatabaseAsync(path);

            var repository =
                new SqliteMedicationRepository(path);

            await repository.AddMedicationRecordAsync(
                Medication("1", "Trazodone HCl", "Active", 429));

            await repository.AddMedicationRecordAsync(
                Medication("2", "Bupropion HCl", "Active", 428));

            await repository.AddMedicationRecordAsync(
                Medication(
                    "3",
                    "Melatonin",
                    MedicationStatuses.Discontinued,
                    427));

            using var output = new StringWriter();

            var exitCode =
                await VeteransConsoleCommand
                    .RunEvidenceCurrentMedicationsAsync(
                        path,
                        new VeteranId("veteran-med-001"),
                        output);

            var rendered = output.ToString();

            Assert.Equal(0, exitCode);
            Assert.Contains("Current Medications : 2", rendered);
            Assert.Contains("Medication  : Bupropion HCl", rendered);
            Assert.Contains("Medication  : Trazodone HCl", rendered);
            Assert.DoesNotContain("Melatonin", rendered);

            Assert.True(
                rendered.IndexOf(
                    "Bupropion HCl",
                    StringComparison.Ordinal) <
                rendered.IndexOf(
                    "Trazodone HCl",
                    StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task EvidenceCurrentMedications_RejectsMissingVeteran()
    {
        var path = Path.GetTempFileName();

        try
        {
            await SeedMedicationTestDatabaseAsync(path);

            using var output = new StringWriter();

            var exitCode =
                await VeteransConsoleCommand
                    .RunEvidenceCurrentMedicationsAsync(
                        path,
                        new VeteranId("missing-veteran"),
                        output);

            Assert.Equal(2, exitCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task EvidenceCurrentMedications_RoutesThroughRunAsync()
    {
        var missing =
            Path.Combine(
                Path.GetTempPath(),
                $"emf-missing-{Guid.NewGuid():N}.db");

        var exitCode =
            await VeteransConsoleCommand.RunAsync(
                [
                    "evidence",
                    "medication",
                    "current",
                    missing,
                    "veteran-med-001"
                ]);

        Assert.Equal(2, exitCode);
    }

    private static MedicationRecord Medication(
        string id,
        string name,
        string status,
        int page) =>
        new()
        {
            Id = new MedicationRecordId(id),
            VeteranId = new VeteranId("veteran-med-001"),
            SourceArtifactId =
                new ArtifactId("blue-button-med-001"),
            RecordDate = new DateOnly(2026, 8, 4),
            SourcePage = page,
            MedicationName = name,
            Status = status
        };
}
