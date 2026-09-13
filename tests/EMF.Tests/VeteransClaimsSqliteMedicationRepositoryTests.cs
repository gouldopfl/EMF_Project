using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Medications;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

namespace EMF.Tests;

public sealed class VeteransClaimsSqliteMedicationRepositoryTests
{
    [Fact]
    public void Repository_ImplementsMedicationContract()
    {
        IMedicationRepository repository =
            new SqliteMedicationRepository("test.db");

        Assert.NotNull(repository);
    }

    [Fact]
    public async Task Repository_RoundTripsMedicationRecord()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository = await CreateAsync(path);

            var record = CreateRecord(
                "med-001",
                new DateOnly(2026, 9, 9),
                428,
                "Bupropion HCl",
                "300MG",
                "TAKE ONE TABLET ORALLY EVERY MORNING",
                "FOR MOOD",
                MedicationStatuses.Active,
                "VA");

            await repository.AddMedicationRecordAsync(record);

            var stored =
                await repository.GetMedicationRecordAsync(record.Id);

            Assert.NotNull(stored);
            Assert.Equal(record.Id, stored!.Id);
            Assert.Equal(record.VeteranId, stored.VeteranId);
            Assert.Equal(record.SourceArtifactId, stored.SourceArtifactId);
            Assert.Equal(record.RecordDate, stored.RecordDate);
            Assert.Equal(record.SourcePage, stored.SourcePage);
            Assert.Equal(record.MedicationName, stored.MedicationName);
            Assert.Equal(record.Strength, stored.Strength);
            Assert.Equal(record.Directions, stored.Directions);
            Assert.Equal(record.Indication, stored.Indication);
            Assert.Equal(record.Status, stored.Status);
            Assert.Equal(record.SourceDesignation, stored.SourceDesignation);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Repository_ReturnsNewestRecordsFirst()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository = await CreateAsync(path);

            await repository.AddMedicationRecordAsync(
                CreateRecord(
                    "med-old",
                    new DateOnly(2025, 1, 1),
                    100,
                    "Trazodone HCl"));

            await repository.AddMedicationRecordAsync(
                CreateRecord(
                    "med-new",
                    new DateOnly(2026, 9, 9),
                    429,
                    "Trazodone HCl"));

            var records =
                await repository.GetMedicationRecordsAsync(
                    new VeteranId("veteran-001"));

            Assert.Equal(2, records.Count);
            Assert.Equal("med-new", records[0].Id.Value);
            Assert.Equal("med-old", records[1].Id.Value);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Repository_LookupIsCaseInsensitive()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository = await CreateAsync(path);

            await repository.AddMedicationRecordAsync(
                CreateRecord(
                    "med-001",
                    new DateOnly(2026, 9, 9),
                    429,
                    "Trazodone HCl"));

            var records =
                await repository.GetMedicationRecordsAsync(
                    new VeteranId("veteran-001"),
                    "trazodone hcl");

            Assert.Single(records);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Repository_RoundTripsNullableFields()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository = await CreateAsync(path);
            var record = CreateRecord(
                "med-001",
                new DateOnly(2026, 9, 9),
                429,
                "Trazodone HCl");

            await repository.AddMedicationRecordAsync(record);

            var stored =
                await repository.GetMedicationRecordAsync(record.Id);

            Assert.NotNull(stored);
            Assert.Null(stored!.Strength);
            Assert.Null(stored.Directions);
            Assert.Null(stored.Indication);
            Assert.Null(stored.SourceDesignation);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Repository_RejectsInvalidSourcePage()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository = await CreateAsync(path);

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => repository.AddMedicationRecordAsync(
                    CreateRecord(
                        "med-001",
                        new DateOnly(2026, 9, 9),
                        0,
                        "Trazodone HCl")));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Repository_RejectsMissingVeteran()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteMedicationRepository(path);

            await repository.InitializeAsync();

            var record = CreateRecord(
                "med-001",
                new DateOnly(2026, 9, 9),
                429,
                "Trazodone HCl");

            await Assert.ThrowsAsync<
                Microsoft.Data.Sqlite.SqliteException>(
                    () => repository.AddMedicationRecordAsync(record));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<SqliteMedicationRepository>
        CreateAsync(string path)
    {
        var repository =
            new SqliteMedicationRepository(path);

        await repository.InitializeAsync();

        await new SqliteVeteranRepository(path)
            .AddVeteranAsync(
                new Veteran
                {
                    Id = new VeteranId("veteran-001")
                });

        return repository;
    }

    private static MedicationRecord CreateRecord(
        string id,
        DateOnly date,
        int page,
        string name,
        string? strength = null,
        string? directions = null,
        string? indication = null,
        string status = MedicationStatuses.Active,
        string? designation = null)
    {
        return new MedicationRecord
        {
            Id = new MedicationRecordId(id),
            VeteranId = new VeteranId("veteran-001"),
            SourceArtifactId =
                new ArtifactId("blue-button-001"),
            RecordDate = date,
            SourcePage = page,
            MedicationName = name,
            Strength = strength,
            Directions = directions,
            Indication = indication,
            Status = status,
            SourceDesignation = designation
        };
    }
    [Fact]
    public async Task Repository_RoundTripsMedicationHistoryEvent()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository = await CreateAsync(path);

            var historyEvent =
                CreateHistoryEvent(
                    "history-001",
                    new DateOnly(2022, 12, 29),
                    2448,
                    "Trazodone HCl",
                    MedicationHistoryEventTypes.LastReleased,
                    "100MG",
                    "TAKE TWO TABLETS ORALLY AT BEDTIME FOR SLEEP",
                    "FOR SLEEP",
                    "10608899D");

            await repository.AddMedicationHistoryEventAsync(historyEvent);

            var stored =
                Assert.Single(
                    await repository.GetMedicationHistoryEventsAsync(
                        new VeteranId("veteran-001"),
                        "Trazodone HCl"));

            Assert.Equal(historyEvent.Id, stored.Id);
            Assert.Equal(historyEvent.EventDate, stored.EventDate);
            Assert.Equal(historyEvent.SourcePage, stored.SourcePage);
            Assert.Equal(historyEvent.MedicationName, stored.MedicationName);
            Assert.Equal(historyEvent.EventType, stored.EventType);
            Assert.Equal(historyEvent.Strength, stored.Strength);
            Assert.Equal(historyEvent.Directions, stored.Directions);
            Assert.Equal(historyEvent.PharmacyIndication, stored.PharmacyIndication);
            Assert.Equal(historyEvent.PrescriptionNumber, stored.PrescriptionNumber);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Repository_ReturnsMedicationHistoryChronologically()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository = await CreateAsync(path);

            await repository.AddMedicationHistoryEventAsync(
                CreateHistoryEvent(
                    "history-new",
                    new DateOnly(2025, 5, 22),
                    990,
                    "Trazodone HCl"));

            await repository.AddMedicationHistoryEventAsync(
                CreateHistoryEvent(
                    "history-old",
                    new DateOnly(2022, 12, 29),
                    2448,
                    "Trazodone HCl"));

            var events =
                await repository.GetMedicationHistoryEventsAsync(
                    new VeteranId("veteran-001"),
                    "Trazodone HCl");

            Assert.Equal(2, events.Count);
            Assert.Equal("history-old", events[0].Id.Value);
            Assert.Equal("history-new", events[1].Id.Value);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Repository_MedicationHistoryLookupIsCaseInsensitive()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository = await CreateAsync(path);

            await repository.AddMedicationHistoryEventAsync(
                CreateHistoryEvent(
                    "history-001",
                    new DateOnly(2023, 1, 4),
                    2445,
                    "Lamotrigine"));

            var events =
                await repository.GetMedicationHistoryEventsAsync(
                    new VeteranId("veteran-001"),
                    "LAMOTRIGINE");

            Assert.Single(events);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Repository_RejectsInvalidMedicationHistorySourcePage()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository = await CreateAsync(path);

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => repository.AddMedicationHistoryEventAsync(
                    CreateHistoryEvent(
                        "history-001",
                        new DateOnly(2022, 12, 29),
                        0,
                        "Trazodone HCl")));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static MedicationHistoryEvent CreateHistoryEvent(
        string id,
        DateOnly date,
        int page,
        string name,
        string eventType = MedicationHistoryEventTypes.LastReleased,
        string? strength = null,
        string? directions = null,
        string? pharmacyIndication = null,
        string? prescriptionNumber = null) =>
        new()
        {
            Id = new MedicationHistoryEventId(id),
            VeteranId = new VeteranId("veteran-001"),
            SourceArtifactId =
                new ArtifactId("blue-button-001"),
            EventDate = date,
            SourcePage = page,
            MedicationName = name,
            EventType = eventType,
            Strength = strength,
            Directions = directions,
            PharmacyIndication = pharmacyIndication,
            PrescriptionNumber = prescriptionNumber
        };

}
