using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Medications;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class ReconciledCurrentMedicationLedgerServiceTests
{
    [Fact]
    public async Task GetAsync_AppliesLatestReconciliationToCurrentLedgerEntry()
    {
        var path = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();

            var veteran = new Veteran { Id = new VeteranId("veteran-1") };
            await new SqliteVeteranRepository(path).AddVeteranAsync(veteran);

            var ledger = new MedicationLedger
            {
                Id = new MedicationLedgerId("ledger-1"),
                VeteranId = veteran.Id,
                SourceArtifactId = new ArtifactId("blue-button"),
                ReportDate = new DateOnly(2026, 9, 9),
                SourceStartPage = 1,
                SourceEndPage = 3,
                ReportedEntryCount = 3,
                ParsedEntryCount = 3,
                IsComplete = true
            };

            var medications = new SqliteMedicationRepository(path);
            await medications.AddMedicationLedgerAsync(
                ledger,
                [
                    Entry(ledger, 1, "Bupropion"),
                    Entry(ledger, 2, "Cyclosporine"),
                    Entry(ledger, 3, "Moxifloxacin")
                ]);

            await medications.AddMedicationCurrentUseReconciliationAsync(
                Reconciliation(
                    veteran.Id,
                    "recon-old",
                    "entry-2",
                    new DateOnly(2026, 9, 14),
                    MedicationCurrentUseStatuses.CurrentlyUsed));

            await medications.AddMedicationCurrentUseReconciliationAsync(
                Reconciliation(
                    veteran.Id,
                    "recon-new",
                    "entry-2",
                    new DateOnly(2026, 9, 15),
                    MedicationCurrentUseStatuses.NotCurrentlyUsed));

            await medications.AddMedicationCurrentUseReconciliationAsync(
                Reconciliation(
                    veteran.Id,
                    "recon-moxi",
                    "entry-3",
                    new DateOnly(2026, 9, 15),
                    MedicationCurrentUseStatuses.NotCurrentlyUsed));

            var result =
                await new ReconciledCurrentMedicationLedgerService(
                        new CurrentMedicationLedgerService(medications),
                        medications)
                    .GetAsync(veteran.Id);

            Assert.NotNull(result);
            var remaining = Assert.Single(result!.Entries);
            Assert.Equal("Bupropion", remaining.MedicationName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static MedicationLedgerEntry Entry(
        MedicationLedger ledger,
        int ordinal,
        string name) =>
        new()
        {
            Id = new MedicationLedgerEntryId($"entry-{ordinal}"),
            MedicationLedgerId = ledger.Id,
            EntryOrdinal = ordinal,
            SourceStartPage = ordinal,
            SourceEndPage = ordinal,
            MedicationName = name,
            Status = MedicationLedgerStatuses.Active
        };

    private static MedicationCurrentUseReconciliation Reconciliation(
        VeteranId veteranId,
        string id,
        string entryId,
        DateOnly date,
        string status) =>
        new()
        {
            Id = new MedicationCurrentUseReconciliationId(id),
            VeteranId = veteranId,
            MedicationLedgerEntryId = new MedicationLedgerEntryId(entryId),
            ReconciliationDate = date,
            CurrentUseStatus = status,
            Source = "VeteranReported",
            Note = "Veteran current-use confirmation."
        };
}
