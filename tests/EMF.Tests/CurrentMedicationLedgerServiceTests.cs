using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Medications;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class CurrentMedicationLedgerServiceTests
{
    private static readonly VeteranId TestVeteranId = new("veteran-001");

    [Fact]
    public async Task GetAsync_ReturnsCurrentEntriesFromNewestCompleteLedger()
    {
        var older = Ledger("older", 2026, 8, 1, true, 1);
        var current = Ledger("current", 2026, 9, 9, true, 5);
        var incomplete = Ledger("incomplete", 2026, 9, 10, false, 1);

        var service = CreateService(
            [older, current, incomplete],
            new Dictionary<string, IReadOnlyList<MedicationLedgerEntry>>
            {
                ["older"] = [Entry(older, 1, "Old", "active")],
                ["current"] =
                [
                    Entry(current, 1, "Trazodone", "active"),
                    Entry(current, 2, "Isosorbide", "refillinprocess"),
                    Entry(current, 3, "Legacy A", "transferred"),
                    Entry(current, 4, "Legacy B", "discontinued"),
                    Entry(current, 5, "Legacy C", "expired")
                ],
                ["incomplete"] = [Entry(incomplete, 1, "Newer", "active")]
            });

        var result = await service.GetAsync(TestVeteranId);

        Assert.NotNull(result);
        Assert.Equal("current", result.Ledger.Id.Value);
        Assert.Collection(
            result.Entries,
            entry =>
            {
                Assert.Equal("Isosorbide", entry.MedicationName);
                Assert.Equal("refillinprocess", entry.Status);
            },
            entry =>
            {
                Assert.Equal("Trazodone", entry.MedicationName);
                Assert.Equal("active", entry.Status);
            });
    }

    [Fact]
    public async Task GetAsync_ReturnsNullWithoutCompleteLedger()
    {
        var ledger = Ledger("incomplete", 2026, 9, 9, false, 1);
        var service = CreateService(
            [ledger],
            new Dictionary<string, IReadOnlyList<MedicationLedgerEntry>>
            {
                ["incomplete"] = [Entry(ledger, 1, "Trazodone", "active")]
            });

        Assert.Null(await service.GetAsync(TestVeteranId));
    }

    [Fact]
    public async Task GetAsync_RejectsAmbiguousNewestCompleteLedger()
    {
        var first = Ledger("first", 2026, 9, 9, true, 1);
        var second = Ledger("second", 2026, 9, 9, true, 1);
        var service = CreateService(
            [first, second],
            new Dictionary<string, IReadOnlyList<MedicationLedgerEntry>>());

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.GetAsync(TestVeteranId));
    }

    [Fact]
    public async Task GetAsync_RejectsPersistedEntryCountMismatch()
    {
        var ledger = Ledger("ledger", 2026, 9, 9, true, 2);
        var service = CreateService(
            [ledger],
            new Dictionary<string, IReadOnlyList<MedicationLedgerEntry>>
            {
                ["ledger"] = [Entry(ledger, 1, "Trazodone", "active")]
            });

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.GetAsync(TestVeteranId));
    }

    [Fact]
    public async Task GetAsync_RejectsUnknownStatus()
    {
        var ledger = Ledger("ledger", 2026, 9, 9, true, 1);
        var service = CreateService(
            [ledger],
            new Dictionary<string, IReadOnlyList<MedicationLedgerEntry>>
            {
                ["ledger"] = [Entry(ledger, 1, "Trazodone", "mystery")]
            });

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.GetAsync(TestVeteranId));
    }

    [Fact]
    public async Task GetAsync_RejectsVeteranMismatch()
    {
        var ledger = new MedicationLedger
        {
            Id = new MedicationLedgerId("ledger"),
            VeteranId = new VeteranId("wrong-veteran"),
            SourceArtifactId = new ArtifactId("blue-button"),
            ReportDate = new DateOnly(2026, 9, 9),
            SourceStartPage = 1,
            SourceEndPage = 2,
            ReportedEntryCount = 1,
            ParsedEntryCount = 1,
            IsComplete = true
        };

        var service = CreateService(
            [ledger],
            new Dictionary<string, IReadOnlyList<MedicationLedgerEntry>>());

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.GetAsync(TestVeteranId));
    }

    private static CurrentMedicationLedgerService CreateService(
        IReadOnlyList<MedicationLedger> ledgers,
        IReadOnlyDictionary<string, IReadOnlyList<MedicationLedgerEntry>> entries) =>
        new(new FakeMedicationRepository(ledgers, entries));

    private static MedicationLedger Ledger(
        string id,
        int year,
        int month,
        int day,
        bool complete,
        int count) =>
        new()
        {
            Id = new MedicationLedgerId(id),
            VeteranId = TestVeteranId,
            SourceArtifactId = new ArtifactId($"source-{id}"),
            ReportDate = new DateOnly(year, month, day),
            SourceStartPage = 1,
            SourceEndPage = 2,
            ReportedEntryCount = count,
            ParsedEntryCount = count,
            IsComplete = complete
        };

    private static MedicationLedgerEntry Entry(
        MedicationLedger ledger,
        int ordinal,
        string name,
        string status) =>
        new()
        {
            Id = new MedicationLedgerEntryId($"{ledger.Id.Value}-{ordinal}"),
            MedicationLedgerId = ledger.Id,
            EntryOrdinal = ordinal,
            SourceStartPage = 1,
            SourceEndPage = 1,
            MedicationName = name,
            Status = status
        };

    private sealed class FakeMedicationRepository(
        IReadOnlyList<MedicationLedger> ledgers,
        IReadOnlyDictionary<string, IReadOnlyList<MedicationLedgerEntry>> entries) :
        IMedicationRepository
    {
        public Task AddMedicationRecordAsync(
            MedicationRecord medicationRecord,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MedicationRecord?> GetMedicationRecordAsync(
            MedicationRecordId medicationRecordId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MedicationRecord>> GetMedicationRecordsAsync(
            VeteranId veteranId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MedicationRecord>> GetMedicationRecordsAsync(
            VeteranId veteranId,
            string medicationName,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MedicationLedger>> GetMedicationLedgersAsync(
            VeteranId veteranId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ledgers);

        public Task<IReadOnlyList<MedicationLedgerEntry>>
            GetMedicationLedgerEntriesAsync(
                MedicationLedgerId medicationLedgerId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult(
                entries.TryGetValue(
                    medicationLedgerId.Value,
                    out var value)
                    ? value
                    : (IReadOnlyList<MedicationLedgerEntry>)[]);
    }
}
