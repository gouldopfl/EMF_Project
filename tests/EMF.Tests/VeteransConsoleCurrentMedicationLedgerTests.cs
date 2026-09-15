using EMF.ConsoleApplication;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Medications;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

namespace EMF.Tests;

public sealed partial class VeteransConsoleCommandTests
{
    [Fact]
    public async Task EvidenceCurrentMedicationLedger_ShowsAuthoritativeCurrentList()
    {
        var path = Path.GetTempFileName();

        try
        {
            await SeedMedicationTestDatabaseAsync(path);

            var repository = new SqliteMedicationRepository(path);
            var ledger = new MedicationLedger
            {
                Id = new MedicationLedgerId("ledger-current"),
                VeteranId = new VeteranId("veteran-med-001"),
                SourceArtifactId = new ArtifactId("blue-button-med-001"),
                ReportDate = new DateOnly(2026, 9, 9),
                SourceStartPage = 3911,
                SourceEndPage = 4023,
                ReportedEntryCount = 3,
                ParsedEntryCount = 3,
                IsComplete = true
            };

            await repository.AddMedicationLedgerAsync(
                ledger,
                [
                    Entry(
                        ledger,
                        1,
                        "Trazodone",
                        MedicationLedgerStatuses.Active,
                        "rx-trazodone"),
                    Entry(
                        ledger,
                        2,
                        "Isosorbide",
                        MedicationLedgerStatuses.RefillInProcess,
                        "rx-isosorbide"),
                    Entry(
                        ledger,
                        3,
                        "Legacy Trazodone",
                        MedicationLedgerStatuses.Transferred,
                        "rx-old")
                ]);

            using var output = new StringWriter();

            var exitCode =
                await VeteransConsoleCommand
                    .RunEvidenceCurrentMedicationLedgerAsync(
                        path,
                        new VeteranId("veteran-med-001"),
                        output);

            var rendered = output.ToString();

            Assert.Equal(0, exitCode);
            Assert.Contains("Report Date          : 2026-09-09", rendered);
            Assert.Contains("Source Pages         : 3911-4023", rendered);
            Assert.Contains("Current Prescriptions: 2", rendered);
            Assert.Contains("Medication   : Isosorbide", rendered);
            Assert.Contains("Status       : refillinprocess", rendered);
            Assert.Contains("Prescription : rx-isosorbide", rendered);
            Assert.Contains("Medication   : Trazodone", rendered);
            Assert.DoesNotContain("Legacy Trazodone", rendered);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task EvidenceCurrentMedicationLedger_RoutesThroughRunAsync()
    {
        var missing =
            Path.Combine(
                Path.GetTempPath(),
                $"emf-missing-ledger-current-{Guid.NewGuid():N}.db");

        var exitCode =
            await VeteransConsoleCommand.RunAsync(
                [
                    "evidence",
                    "medication",
                    "ledger",
                    "current",
                    missing,
                    "veteran-med-001"
                ]);

        Assert.Equal(2, exitCode);
        Assert.False(File.Exists(missing));
    }

    private static MedicationLedgerEntry Entry(
        MedicationLedger ledger,
        int ordinal,
        string name,
        string status,
        string prescriptionNumber) =>
        new()
        {
            Id = new MedicationLedgerEntryId($"entry-{ordinal}"),
            MedicationLedgerId = ledger.Id,
            EntryOrdinal = ordinal,
            SourceStartPage = 3911 + ordinal,
            SourceEndPage = 3911 + ordinal,
            MedicationName = name,
            Strength = "100 mg",
            Status = status,
            PrescriptionNumber = prescriptionNumber,
            PrescribedDate = new DateOnly(2026, 8, 21),
            LastFilledOnText = "Not filled yet",
            ExpirationDate = new DateOnly(2027, 8, 1),
            RefillsLeft = 2,
            Directions = "Test directions",
            Indication = "Test indication"
        };
}
