using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Medications;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

namespace EMF.Tests;

public sealed class MedicationCurrentUseReconciliationPersistenceTests
{
    [Fact]
    public async Task Repository_RoundTripsMedicationCurrentUseReconciliation()
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
                SourceEndPage = 1,
                ReportedEntryCount = 1,
                ParsedEntryCount = 1,
                IsComplete = true
            };

            var medications = new SqliteMedicationRepository(path);
            await medications.AddMedicationLedgerAsync(
                ledger,
                [
                    new MedicationLedgerEntry
                    {
                        Id = new MedicationLedgerEntryId("entry-1"),
                        MedicationLedgerId = ledger.Id,
                        EntryOrdinal = 1,
                        SourceStartPage = 1,
                        SourceEndPage = 1,
                        MedicationName = "Cyclosporine ophthalmic",
                        Status = MedicationLedgerStatuses.Active,
                        PrescriptionNumber = "rx-1"
                    }
                ]);

            var reconciliation = new MedicationCurrentUseReconciliation
            {
                Id = new MedicationCurrentUseReconciliationId("recon-1"),
                VeteranId = veteran.Id,
                MedicationLedgerEntryId = new MedicationLedgerEntryId("entry-1"),
                ReconciliationDate = new DateOnly(2026, 9, 15),
                CurrentUseStatus = MedicationCurrentUseStatuses.NotCurrentlyUsed,
                Source = "VeteranReported",
                Note = "No longer using eye drops."
            };

            await medications.AddMedicationCurrentUseReconciliationAsync(
                reconciliation);

            var stored =
                Assert.Single(
                    await medications
                        .GetMedicationCurrentUseReconciliationsAsync(veteran.Id));

            Assert.Equal(reconciliation.Id, stored.Id);
            Assert.Equal(reconciliation.VeteranId, stored.VeteranId);
            Assert.Equal(
                reconciliation.MedicationLedgerEntryId,
                stored.MedicationLedgerEntryId);
            Assert.Equal(
                reconciliation.ReconciliationDate,
                stored.ReconciliationDate);
            Assert.Equal(
                reconciliation.CurrentUseStatus,
                stored.CurrentUseStatus);
            Assert.Equal(reconciliation.Source, stored.Source);
            Assert.Equal(reconciliation.Note, stored.Note);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
