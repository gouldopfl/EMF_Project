using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Medications;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class MedicationIndicationReconciliationPersistenceTests
{
    [Fact]
    public async Task Migration89_UpgradesExistingLedgerOnceAndPreservesAttributedIndicationSeparately()
    {
        var path = Path.GetTempFileName();
        try
        {
            var schema = new VeteransClaimsSqliteSchema(path);
            await schema.InitializeAsync();
            var veteran = new VeteranId("veteran-example");
            await new SqliteVeteranRepository(path).AddVeteranAsync(new Veteran { Id = veteran });
            var repository = new SqliteMedicationRepository(path);
            var ledger = new MedicationLedger
            {
                Id = new("ledger"), VeteranId = veteran, SourceArtifactId = new ArtifactId("source"),
                ReportDate = new(2026, 9, 9), SourceStartPage = 1, SourceEndPage = 1,
                ParsedEntryCount = 1, ReportedEntryCount = 1, IsComplete = true
            };
            await repository.AddMedicationLedgerAsync(ledger, [new MedicationLedgerEntry
            {
                Id = new("entry"), MedicationLedgerId = ledger.Id, EntryOrdinal = 1,
                SourceStartPage = 1, SourceEndPage = 1, MedicationName = "Example medication",
                Status = "active", Directions = "TAKE DAILY FOR MOOD", Indication = "FOR MOOD"
            }]);
            // Reproduce the v88 schema, retaining the pre-existing ledger and migration history.
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText = "DROP TABLE VeteransClaims_MedicationIndicationReconciliations; DELETE FROM VeteransClaims_SchemaMigrations WHERE Version = 89;";
                await command.ExecuteNonQueryAsync();
            }
            await schema.InitializeAsync();
            await schema.InitializeAsync();
            var clarification = new MedicationIndicationReconciliation
            {
                Id = "statement-1", VeteranId = veteran, MedicationName = "Example medication",
                ReconciliationDate = new(2026, 9, 24), Indication = "anxiety",
                Source = "Veteran statement — Robin Example"
            };
            await repository.AddMedicationIndicationReconciliationAsync(clarification);
            var stored = Assert.Single(await repository.GetMedicationIndicationReconciliationsAsync(veteran));
            Assert.Equal(clarification.Source, stored.Source);
            Assert.Equal(clarification.Indication, stored.Indication);
            Assert.Equal(clarification.ReconciliationDate, stored.ReconciliationDate);
            Assert.Empty(await repository.GetMedicationIndicationReconciliationsAsync(new("other-veteran")));
            var original = Assert.Single(await repository.GetMedicationLedgerEntriesAsync(ledger.Id));
            Assert.Equal("TAKE DAILY FOR MOOD", original.Directions);
            Assert.Equal("FOR MOOD", original.Indication);
            Assert.Equal("active", original.Status);
            await Assert.ThrowsAsync<SqliteException>(() => repository.AddMedicationIndicationReconciliationAsync(clarification));
            await using var check = new SqliteConnection($"Data Source={path}");
            await check.OpenAsync();
            var count = check.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM VeteransClaims_SchemaMigrations WHERE Version = 89";
            Assert.Equal(1L, await count.ExecuteScalarAsync());
        }
        finally { File.Delete(path); }
    }
}
