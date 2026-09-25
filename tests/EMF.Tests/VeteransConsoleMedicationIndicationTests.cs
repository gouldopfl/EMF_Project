using EMF.ConsoleApplication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

namespace EMF.Tests;

public sealed class VeteransConsoleMedicationIndicationTests
{
    [Fact]
    public async Task Reconciliation_PersistsStatementAttributionAndRejectsConflictingOverwrite()
    {
        var path = Path.GetTempFileName();
        try
        {
            var repository = new SqliteMedicationRepository(path);
            await repository.InitializeAsync();
            var veteran = new VeteranId("example");
            await new SqliteVeteranRepository(path).AddVeteranAsync(new Veteran { Id = veteran });
            using var output = new StringWriter();
            for (var i = 0; i < 2; i++)
                Assert.Equal(0, await VeteransConsoleCommand.RunEvidenceMedicationIndicationReconciliationAsync(
                    path, veteran, new(2026, 9, 24), "Examplemed", "anxiety", "Veteran statement — Robin Example", output));
            Assert.Equal(2, await VeteransConsoleCommand.RunEvidenceMedicationIndicationReconciliationAsync(
                path, veteran, new(2026, 9, 24), "Examplemed", "different indication", "Other source", output));
            var record = Assert.Single(await repository.GetMedicationIndicationReconciliationsAsync(veteran));
            Assert.Equal("Veteran statement — Robin Example", record.Source);
            Assert.Equal("anxiety", record.Indication);
        }
        finally { File.Delete(path); }
    }
}
