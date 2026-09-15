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
    public async Task Repository_RoundTripsMedicationLedgerAndEntries()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository = await CreateAsync(path);
            var ledger = CreateLedger(
                "ledger-001",
                new DateOnly(2026, 9, 9),
                3900,
                4024,
                2,
                true);

            var entries = new[]
            {
                CreateLedgerEntry(
                    "ledger-entry-001",
                    ledger.Id,
                    1,
                    3910,
                    "buPROPion (buPROPion XL 300 mg/24 hour tablet)",
                    "active",
                    prescriptionNumber: "3211-50183021",
                    prescribedDate: new DateOnly(2026, 8, 21),
                    lastFilledDate: new DateOnly(2026, 7, 2),
                    lastFilledOnText: "July 2, 2026",
                    expirationDate: new DateOnly(2027, 2, 28),
                    refillsLeft: 2,
                    directions: "TAKE ONE TABLET ORALLY EVERY MORNING",
                    indication: "FOR MOOD",
                    prescriber: "CLARK, DAVID G, MD",
                    facility: "VA",
                    quantity: "90"),
                CreateLedgerEntry(
                    "ledger-entry-002",
                    ledger.Id,
                    2,
                    3920,
                    "isosorbide mononitrate (isosorbide mononitrate ER 30 mg/24 hour tablet)",
                    "refillinprocess",
                    prescriptionNumber: "3211-50014120",
                    prescribedDate: new DateOnly(2026, 8, 21),
                    lastFilledOnText: "Not filled yet",
                    directions: "TAKE ONE TABLET ORALLY EVERY DAY WITH BREAKFAST FOR PREVENTING CHEST PAIN")
            };

            await repository.AddMedicationLedgerAsync(ledger, entries);

            var storedLedger =
                await repository.GetMedicationLedgerAsync(ledger.Id);
            var storedEntries =
                await repository.GetMedicationLedgerEntriesAsync(ledger.Id);

            Assert.NotNull(storedLedger);
            Assert.Equal(ledger.Id, storedLedger!.Id);
            Assert.Equal(ledger.VeteranId, storedLedger.VeteranId);
            Assert.Equal(ledger.SourceArtifactId, storedLedger.SourceArtifactId);
            Assert.Equal(ledger.ReportDate, storedLedger.ReportDate);
            Assert.Equal(ledger.SourceStartPage, storedLedger.SourceStartPage);
            Assert.Equal(ledger.SourceEndPage, storedLedger.SourceEndPage);
            Assert.Equal(ledger.ReportedEntryCount, storedLedger.ReportedEntryCount);
            Assert.Equal(ledger.ParsedEntryCount, storedLedger.ParsedEntryCount);
            Assert.True(storedLedger.IsComplete);

            Assert.Equal(2, storedEntries.Count);
            Assert.Equal("active", storedEntries[0].Status);
            Assert.Equal("3211-50183021", storedEntries[0].PrescriptionNumber);
            Assert.Equal(new DateOnly(2026, 7, 2), storedEntries[0].LastFilledDate);
            Assert.Equal("July 2, 2026", storedEntries[0].LastFilledOnText);
            Assert.Equal(2, storedEntries[0].RefillsLeft);
            Assert.Equal("refillinprocess", storedEntries[1].Status);
            Assert.Null(storedEntries[1].LastFilledDate);
            Assert.Equal("Not filled yet", storedEntries[1].LastFilledOnText);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Repository_ReturnsMedicationLedgersNewestFirst()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository = await CreateAsync(path);

            await repository.AddMedicationLedgerAsync(
                CreateLedger(
                    "ledger-old",
                    new DateOnly(2025, 9, 9),
                    3800,
                    3801,
                    0,
                    true),
                []);

            await repository.AddMedicationLedgerAsync(
                CreateLedger(
                    "ledger-new",
                    new DateOnly(2026, 9, 9),
                    3900,
                    3901,
                    0,
                    true),
                []);

            var ledgers =
                await repository.GetMedicationLedgersAsync(
                    new VeteranId("veteran-001"));

            Assert.Equal(2, ledgers.Count);
            Assert.Equal("ledger-new", ledgers[0].Id.Value);
            Assert.Equal("ledger-old", ledgers[1].Id.Value);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Repository_RejectsMedicationLedgerEntryCountMismatch()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository = await CreateAsync(path);
            var ledger = CreateLedger(
                "ledger-001",
                new DateOnly(2026, 9, 9),
                3900,
                4024,
                2,
                true);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => repository.AddMedicationLedgerAsync(
                    ledger,
                    [
                        CreateLedgerEntry(
                            "ledger-entry-001",
                            ledger.Id,
                            1,
                            3910,
                            "Trazodone",
                            "active")
                    ]));

            Assert.Null(
                await repository.GetMedicationLedgerAsync(ledger.Id));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static MedicationLedger CreateLedger(
        string id,
        DateOnly reportDate,
        int startPage,
        int endPage,
        int parsedEntryCount,
        bool isComplete) =>
        new()
        {
            Id = new MedicationLedgerId(id),
            VeteranId = new VeteranId("veteran-001"),
            SourceArtifactId = new ArtifactId("blue-button-001"),
            ReportDate = reportDate,
            SourceStartPage = startPage,
            SourceEndPage = endPage,
            ReportedEntryCount = parsedEntryCount,
            ParsedEntryCount = parsedEntryCount,
            IsComplete = isComplete
        };

    private static MedicationLedgerEntry CreateLedgerEntry(
        string id,
        MedicationLedgerId ledgerId,
        int ordinal,
        int page,
        string name,
        string status,
        string? prescriptionNumber = null,
        DateOnly? prescribedDate = null,
        DateOnly? lastFilledDate = null,
        string? lastFilledOnText = null,
        DateOnly? expirationDate = null,
        int? refillsLeft = null,
        string? directions = null,
        string? indication = null,
        string? prescriber = null,
        string? facility = null,
        string? quantity = null) =>
        new()
        {
            Id = new MedicationLedgerEntryId(id),
            MedicationLedgerId = ledgerId,
            EntryOrdinal = ordinal,
            SourceStartPage = page,
            SourceEndPage = page,
            MedicationName = name,
            Strength = null,
            Status = status,
            PrescriptionNumber = prescriptionNumber,
            PrescribedDate = prescribedDate,
            LastFilledDate = lastFilledDate,
            LastFilledOnText = lastFilledOnText,
            ExpirationDate = expirationDate,
            RefillsLeft = refillsLeft,
            Directions = directions,
            Indication = indication,
            Prescriber = prescriber,
            Facility = facility,
            Quantity = quantity
        };

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


    [Fact]
    public async Task Repository_RoundTripsMedicationClinicalContext()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository = await CreateAsync(path);
            var context = CreateClinicalContext(
                "context-001",
                new DateOnly(2025, 8, 5),
                1140,
                1141,
                "Isosorbide Mononitrate",
                "12620234",
                MedicationClinicalContextTypes.ClinicalEffect,
                "Clinical record documents dizziness and loss of balance after the dose increase.");

            await repository.AddMedicationClinicalContextAsync(context);

            var stored =
                Assert.Single(
                    await repository.GetMedicationClinicalContextsAsync(
                        new VeteranId("veteran-001")));

            Assert.Equal(context.Id, stored.Id);
            Assert.Equal(context.VeteranId, stored.VeteranId);
            Assert.Equal(context.SourceArtifactId, stored.SourceArtifactId);
            Assert.Equal(context.EventDate, stored.EventDate);
            Assert.Equal(context.SourceStartPage, stored.SourceStartPage);
            Assert.Equal(context.SourceEndPage, stored.SourceEndPage);
            Assert.Equal(context.MedicationName, stored.MedicationName);
            Assert.Equal(context.PrescriptionNumber, stored.PrescriptionNumber);
            Assert.Equal(context.ContextType, stored.ContextType);
            Assert.Equal(context.Summary, stored.Summary);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Repository_ReturnsMedicationClinicalContextsChronologically()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository = await CreateAsync(path);

            await repository.AddMedicationClinicalContextAsync(
                CreateClinicalContext(
                    "context-new",
                    new DateOnly(2026, 1, 1),
                    1200,
                    1200,
                    "Isosorbide Mononitrate",
                    "12620234",
                    MedicationClinicalContextTypes.ClinicalObservation,
                    "Later observation."));

            await repository.AddMedicationClinicalContextAsync(
                CreateClinicalContext(
                    "context-old",
                    new DateOnly(2025, 8, 5),
                    1140,
                    1141,
                    "Isosorbide Mononitrate",
                    "12620234",
                    MedicationClinicalContextTypes.ClinicalEffect,
                    "Earlier observation."));

            var stored =
                await repository.GetMedicationClinicalContextsAsync(
                    new VeteranId("veteran-001"));

            Assert.Equal(2, stored.Count);
            Assert.Equal("context-old", stored[0].Id.Value);
            Assert.Equal("context-new", stored[1].Id.Value);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Repository_RejectsInvalidMedicationClinicalContextPageRange()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository = await CreateAsync(path);

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => repository.AddMedicationClinicalContextAsync(
                    CreateClinicalContext(
                        "context-001",
                        new DateOnly(2025, 8, 5),
                        1141,
                        1140,
                        "Isosorbide Mononitrate",
                        "12620234",
                        MedicationClinicalContextTypes.ClinicalEffect,
                        "Clinical effect.")));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static MedicationClinicalContext CreateClinicalContext(
        string id,
        DateOnly date,
        int startPage,
        int endPage,
        string medicationName,
        string prescriptionNumber,
        string contextType,
        string summary) =>
        new()
        {
            Id = new MedicationClinicalContextId(id),
            VeteranId = new VeteranId("veteran-001"),
            SourceArtifactId = new ArtifactId("blue-button-001"),
            EventDate = date,
            SourceStartPage = startPage,
            SourceEndPage = endPage,
            MedicationName = medicationName,
            PrescriptionNumber = prescriptionNumber,
            ContextType = contextType,
            Summary = summary
        };

}
