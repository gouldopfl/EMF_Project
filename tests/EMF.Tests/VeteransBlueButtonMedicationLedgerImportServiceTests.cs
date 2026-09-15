using EMF.Common;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

namespace EMF.Tests;

public sealed class VeteransBlueButtonMedicationLedgerImportServiceTests
{
    [Fact]
    public async Task ImportAsync_PersistsParsedLedgerAndEntries()
    {
        var path = Path.GetTempFileName();

        try
        {
            await SeedVeteranAsync(path);

            var repository = new SqliteMedicationRepository(path);
            var service =
                new VeteransBlueButtonMedicationLedgerImportService(
                    repository,
                    new SequenceIdGenerator(
                        "ledger-001",
                        "entry-001",
                        "entry-002"));

            var result =
                await service.ImportAsync(
                    new VeteranId("veteran-med-ledger-import"),
                    new ArtifactId("blue-button-2026-09-09"),
                    ParsedLedger());

            Assert.False(result.AlreadyPersisted);
            Assert.Equal("ledger-001", result.Ledger.Id.Value);
            Assert.True(result.Ledger.IsComplete);
            Assert.Equal(2, result.Entries.Count);

            var stored =
                await repository.GetMedicationLedgerAsync(
                    result.Ledger.Id);

            Assert.NotNull(stored);
            Assert.Equal(
                new DateOnly(2026, 9, 9),
                stored.ReportDate);
            Assert.Equal(3900, stored.SourceStartPage);
            Assert.Equal(4024, stored.SourceEndPage);
            Assert.Equal(2, stored.ReportedEntryCount);
            Assert.Equal(2, stored.ParsedEntryCount);

            var entries =
                await repository.GetMedicationLedgerEntriesAsync(
                    result.Ledger.Id);

            Assert.Collection(
                entries,
                entry =>
                {
                    Assert.Equal("entry-001", entry.Id.Value);
                    Assert.Equal("active", entry.Status);
                    Assert.Equal("3211-50183021", entry.PrescriptionNumber);
                    Assert.Equal(
                        new DateOnly(2026, 7, 2),
                        entry.LastFilledDate);
                },
                entry =>
                {
                    Assert.Equal("entry-002", entry.Id.Value);
                    Assert.Equal("refillinprocess", entry.Status);
                    Assert.Null(entry.LastFilledDate);
                    Assert.Equal("Not filled yet", entry.LastFilledOnText);
                });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ImportAsync_ReplaysSameArtifactIdempotently()
    {
        var path = Path.GetTempFileName();

        try
        {
            await SeedVeteranAsync(path);

            var repository = new SqliteMedicationRepository(path);
            var ids =
                new SequenceIdGenerator(
                    "ledger-001",
                    "entry-001",
                    "entry-002");
            var service =
                new VeteransBlueButtonMedicationLedgerImportService(
                    repository,
                    ids);

            var first =
                await service.ImportAsync(
                    new VeteranId("veteran-med-ledger-import"),
                    new ArtifactId("blue-button-2026-09-09"),
                    ParsedLedger());

            var replay =
                await service.ImportAsync(
                    new VeteranId("veteran-med-ledger-import"),
                    new ArtifactId("blue-button-2026-09-09"),
                    ParsedLedger());

            Assert.False(first.AlreadyPersisted);
            Assert.True(replay.AlreadyPersisted);
            Assert.Equal(first.Ledger.Id, replay.Ledger.Id);
            Assert.Equal(3, ids.GeneratedCount);

            var ledgers =
                await repository.GetMedicationLedgersAsync(
                    new VeteranId("veteran-med-ledger-import"));

            Assert.Single(ledgers);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ImportAsync_RejectsChangedParseForExistingSource()
    {
        var path = Path.GetTempFileName();

        try
        {
            await SeedVeteranAsync(path);

            var repository = new SqliteMedicationRepository(path);
            var service =
                new VeteransBlueButtonMedicationLedgerImportService(
                    repository,
                    new SequenceIdGenerator(
                        "ledger-001",
                        "entry-001",
                        "entry-002"));

            await service.ImportAsync(
                new VeteranId("veteran-med-ledger-import"),
                new ArtifactId("blue-button-2026-09-09"),
                ParsedLedger());

            var changed = ParsedLedger("discontinued");

            var ex =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () =>
                        service.ImportAsync(
                            new VeteranId("veteran-med-ledger-import"),
                            new ArtifactId("blue-button-2026-09-09"),
                            changed));

            Assert.Equal(
                "The source artifact already has a medication ledger with different parsed content.",
                ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static VeteransBlueButtonMedicationLedgerParseResult ParsedLedger(
        string firstStatus = "active") =>
        new()
        {
            ReportDate = new DateOnly(2026, 9, 9),
            SourceStartPage = 3900,
            SourceEndPage = 4024,
            ReportedEntryCount = 2,
            Entries =
            [
                new VeteransBlueButtonMedicationLedgerEntry
                {
                    EntryOrdinal = 1,
                    SourceStartPage = 3910,
                    SourceEndPage = 3910,
                    MedicationName =
                        "buPROPion (buPROPion XL 300 mg/24 hour tablet)",
                    Strength = "300 mg/24 hour",
                    Status = firstStatus,
                    PrescriptionNumber = "3211-50183021",
                    PrescribedDate = new DateOnly(2026, 8, 21),
                    LastFilledDate = new DateOnly(2026, 7, 2),
                    LastFilledOnText = "July 2, 2026",
                    ExpirationDate = new DateOnly(2027, 2, 28),
                    RefillsLeft = 2,
                    Directions = "TAKE ONE TABLET ORALLY EVERY MORNING FOR MOOD",
                    Indication = "FOR MOOD",
                    Prescriber = "CLARK, DAVID G, MD",
                    Facility = "Roudebush VAMC",
                    Quantity = "90"
                },
                new VeteransBlueButtonMedicationLedgerEntry
                {
                    EntryOrdinal = 2,
                    SourceStartPage = 3911,
                    SourceEndPage = 3911,
                    MedicationName =
                        "isosorbide mononitrate (isosorbide mononitrate ER 30 mg/24 hour tablet)",
                    Strength = "30 mg/24 hour",
                    Status = "refillinprocess",
                    PrescriptionNumber = "3211-50014120",
                    PrescribedDate = new DateOnly(2026, 8, 21),
                    LastFilledDate = null,
                    LastFilledOnText = "Not filled yet",
                    ExpirationDate = new DateOnly(2027, 8, 17),
                    RefillsLeft = 3,
                    Directions =
                        "TAKE ONE TABLET ORALLY EVERY DAY WITH BREAKFAST FOR PREVENTING CHEST PAIN",
                    Indication = "FOR PREVENTING CHEST PAIN",
                    Prescriber = "POWELL, LYNN M, NP",
                    Facility = "Roudebush VAMC",
                    Quantity = "90"
                }
            ]
        };

    private static async Task SeedVeteranAsync(string path)
    {
        await new VeteransClaimsSqliteSchema(path)
            .InitializeAsync();

        await new SqliteVeteranRepository(path)
            .AddVeteranAsync(
                new Veteran
                {
                    Id = new VeteranId("veteran-med-ledger-import")
                });
    }

    private sealed class SequenceIdGenerator : IIdGenerator
    {
        private readonly Queue<string> _values;

        public SequenceIdGenerator(params string[] values)
        {
            _values = new Queue<string>(values);
        }

        public int GeneratedCount { get; private set; }

        public string Generate()
        {
            GeneratedCount++;
            return _values.Dequeue();
        }
    }
}
