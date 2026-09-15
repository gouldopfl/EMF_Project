using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Medications;
using Microsoft.Data.Sqlite;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

public sealed class SqliteMedicationRepository :
    IMedicationRepository
{
    private readonly string _databasePath;

    public SqliteMedicationRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = databasePath;
    }

    private SqliteConnection CreateConnection() =>
        VeteransClaimsSqliteConnectionFactory.Create(_databasePath);

    public Task InitializeAsync(
        CancellationToken cancellationToken = default) =>
        new VeteransClaimsSqliteSchema(_databasePath)
            .InitializeAsync(cancellationToken);

    public async Task AddMedicationRecordAsync(
        MedicationRecord medicationRecord,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(medicationRecord);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            medicationRecord.MedicationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            medicationRecord.Status);

        if (medicationRecord.SourcePage <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(medicationRecord.SourcePage));

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VeteransClaims_MedicationRecords (
                Id, VeteranId, SourceArtifactId, RecordDate,
                SourcePage, MedicationName, Strength,
                Directions, Indication, Status, SourceDesignation
            )
            VALUES (
                $id, $veteranId, $sourceArtifactId, $recordDate,
                $sourcePage, $medicationName, $strength,
                $directions, $indication, $status, $sourceDesignation
            );
            """;

        command.Parameters.AddWithValue("$id", medicationRecord.Id.Value);
        command.Parameters.AddWithValue("$veteranId", medicationRecord.VeteranId.Value);
        command.Parameters.AddWithValue("$sourceArtifactId", medicationRecord.SourceArtifactId.Value);
        command.Parameters.AddWithValue("$recordDate", medicationRecord.RecordDate.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$sourcePage", medicationRecord.SourcePage);
        command.Parameters.AddWithValue("$medicationName", medicationRecord.MedicationName);
        command.Parameters.AddWithValue("$strength", (object?)medicationRecord.Strength ?? DBNull.Value);
        command.Parameters.AddWithValue("$directions", (object?)medicationRecord.Directions ?? DBNull.Value);
        command.Parameters.AddWithValue("$indication", (object?)medicationRecord.Indication ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", medicationRecord.Status);
        command.Parameters.AddWithValue("$sourceDesignation", (object?)medicationRecord.SourceDesignation ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<MedicationRecord?> GetMedicationRecordAsync(
        MedicationRecordId medicationRecordId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, VeteranId, SourceArtifactId, RecordDate,
                   SourcePage, MedicationName, Strength,
                   Directions, Indication, Status, SourceDesignation
            FROM VeteransClaims_MedicationRecords
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            medicationRecordId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? ReadMedicationRecord(reader)
            : null;
    }

    public Task<IReadOnlyList<MedicationRecord>>
        GetMedicationRecordsAsync(
            VeteranId veteranId,
            CancellationToken cancellationToken = default)
    {
        return GetMedicationRecordsCoreAsync(
            veteranId,
            null,
            cancellationToken);
    }

    public Task<IReadOnlyList<MedicationRecord>>
        GetMedicationRecordsAsync(
            VeteranId veteranId,
            string medicationName,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            medicationName);

        return GetMedicationRecordsCoreAsync(
            veteranId,
            medicationName,
            cancellationToken);
    }

    private async Task<IReadOnlyList<MedicationRecord>>
        GetMedicationRecordsCoreAsync(
            VeteranId veteranId,
            string? medicationName,
            CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText = medicationName is null
            ? """
              SELECT Id, VeteranId, SourceArtifactId, RecordDate,
                     SourcePage, MedicationName, Strength,
                     Directions, Indication, Status, SourceDesignation
              FROM VeteransClaims_MedicationRecords
              WHERE VeteranId = $veteranId
              ORDER BY RecordDate DESC, Id;
              """
            : """
              SELECT Id, VeteranId, SourceArtifactId, RecordDate,
                     SourcePage, MedicationName, Strength,
                     Directions, Indication, Status, SourceDesignation
              FROM VeteransClaims_MedicationRecords
              WHERE VeteranId = $veteranId
                AND MedicationName = $medicationName COLLATE NOCASE
              ORDER BY RecordDate DESC, Id;
              """;

        command.Parameters.AddWithValue(
            "$veteranId",
            veteranId.Value);

        if (medicationName is not null)
        {
            command.Parameters.AddWithValue(
                "$medicationName",
                medicationName);
        }

        var records = new List<MedicationRecord>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
            records.Add(ReadMedicationRecord(reader));

        return records;
    }

    public async Task AddMedicationLedgerAsync(
        MedicationLedger medicationLedger,
        IReadOnlyCollection<MedicationLedgerEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(medicationLedger);
        ArgumentNullException.ThrowIfNull(entries);

        if (medicationLedger.SourceStartPage <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(medicationLedger.SourceStartPage));

        if (medicationLedger.SourceEndPage <
            medicationLedger.SourceStartPage)
        {
            throw new ArgumentOutOfRangeException(
                nameof(medicationLedger.SourceEndPage));
        }

        if (medicationLedger.ReportedEntryCount < 0)
            throw new ArgumentOutOfRangeException(
                nameof(medicationLedger.ReportedEntryCount));

        if (medicationLedger.ParsedEntryCount < 0)
            throw new ArgumentOutOfRangeException(
                nameof(medicationLedger.ParsedEntryCount));

        if (medicationLedger.ParsedEntryCount != entries.Count)
        {
            throw new InvalidOperationException(
                "Medication ledger parsed-entry count must match " +
                "the entries being persisted.");
        }

        var ordinals = new HashSet<int>();

        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);

            if (entry.MedicationLedgerId != medicationLedger.Id)
            {
                throw new InvalidOperationException(
                    "Every medication ledger entry must reference " +
                    "the medication ledger being persisted.");
            }

            if (entry.EntryOrdinal <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(entry.EntryOrdinal));

            if (!ordinals.Add(entry.EntryOrdinal))
            {
                throw new InvalidOperationException(
                    "Medication ledger entry ordinals must be unique.");
            }

            if (entry.SourceStartPage < medicationLedger.SourceStartPage ||
                entry.SourceStartPage > medicationLedger.SourceEndPage)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(entry.SourceStartPage));
            }

            if (entry.SourceEndPage < entry.SourceStartPage ||
                entry.SourceEndPage > medicationLedger.SourceEndPage)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(entry.SourceEndPage));
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(
                entry.MedicationName);
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.Status);

            if (entry.RefillsLeft < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(entry.RefillsLeft));
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            (SqliteTransaction)
            await connection.BeginTransactionAsync(cancellationToken);

        await InsertMedicationLedgerAsync(
            connection,
            transaction,
            medicationLedger,
            cancellationToken);

        foreach (var entry in entries.OrderBy(entry => entry.EntryOrdinal))
        {
            await InsertMedicationLedgerEntryAsync(
                connection,
                transaction,
                entry,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<MedicationLedger?> GetMedicationLedgerAsync(
        MedicationLedgerId medicationLedgerId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT Id, VeteranId, SourceArtifactId, ReportDate,
                   SourceStartPage, SourceEndPage, ReportedEntryCount,
                   ParsedEntryCount, IsComplete
            FROM VeteransClaims_MedicationLedgers
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            medicationLedgerId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? ReadMedicationLedger(reader)
            : null;
    }

    public async Task<IReadOnlyList<MedicationLedger>>
        GetMedicationLedgersAsync(
            VeteranId veteranId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT Id, VeteranId, SourceArtifactId, ReportDate,
                   SourceStartPage, SourceEndPage, ReportedEntryCount,
                   ParsedEntryCount, IsComplete
            FROM VeteransClaims_MedicationLedgers
            WHERE VeteranId = $veteranId
            ORDER BY ReportDate DESC, SourceStartPage DESC, Id;
            """;

        command.Parameters.AddWithValue(
            "$veteranId",
            veteranId.Value);

        var result = new List<MedicationLedger>();
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
            result.Add(ReadMedicationLedger(reader));

        return result;
    }

    public async Task<IReadOnlyList<MedicationLedgerEntry>>
        GetMedicationLedgerEntriesAsync(
            MedicationLedgerId medicationLedgerId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT Id, MedicationLedgerId, EntryOrdinal,
                   SourceStartPage, SourceEndPage, MedicationName,
                   Strength, Status, PrescriptionNumber, PrescribedDate,
                   LastFilledDate, LastFilledOnText, ExpirationDate,
                   RefillsLeft, Directions, Indication, Prescriber,
                   Facility, Quantity
            FROM VeteransClaims_MedicationLedgerEntries
            WHERE MedicationLedgerId = $medicationLedgerId
            ORDER BY EntryOrdinal, Id;
            """;

        command.Parameters.AddWithValue(
            "$medicationLedgerId",
            medicationLedgerId.Value);

        var result = new List<MedicationLedgerEntry>();
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
            result.Add(ReadMedicationLedgerEntry(reader));

        return result;
    }

    public async Task AddMedicationClinicalContextAsync(
        MedicationClinicalContext clinicalContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clinicalContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            clinicalContext.MedicationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            clinicalContext.PrescriptionNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            clinicalContext.ContextType);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            clinicalContext.RecordTitle);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            clinicalContext.Summary);

        if (clinicalContext.SourceStartPage <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(clinicalContext.SourceStartPage));

        if (clinicalContext.SourceEndPage <
            clinicalContext.SourceStartPage)
        {
            throw new ArgumentOutOfRangeException(
                nameof(clinicalContext.SourceEndPage));
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO VeteransClaims_MedicationClinicalContexts (
                Id, VeteranId, SourceArtifactId, EventDate,
                SourceStartPage, SourceEndPage, MedicationName,
                PrescriptionNumber, ContextType, RecordTitle, Summary
            )
            VALUES (
                $id, $veteranId, $sourceArtifactId, $eventDate,
                $sourceStartPage, $sourceEndPage, $medicationName,
                $prescriptionNumber, $contextType, $recordTitle, $summary
            );
            """;

        command.Parameters.AddWithValue(
            "$id", clinicalContext.Id.Value);
        command.Parameters.AddWithValue(
            "$veteranId", clinicalContext.VeteranId.Value);
        command.Parameters.AddWithValue(
            "$sourceArtifactId", clinicalContext.SourceArtifactId.Value);
        command.Parameters.AddWithValue(
            "$eventDate", clinicalContext.EventDate.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue(
            "$sourceStartPage", clinicalContext.SourceStartPage);
        command.Parameters.AddWithValue(
            "$sourceEndPage", clinicalContext.SourceEndPage);
        command.Parameters.AddWithValue(
            "$medicationName", clinicalContext.MedicationName);
        command.Parameters.AddWithValue(
            "$prescriptionNumber", clinicalContext.PrescriptionNumber);
        command.Parameters.AddWithValue(
            "$contextType", clinicalContext.ContextType);
        command.Parameters.AddWithValue(
            "$recordTitle", clinicalContext.RecordTitle);
        command.Parameters.AddWithValue(
            "$summary", clinicalContext.Summary);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateMedicationClinicalContextRecordTitleAsync(
        MedicationClinicalContextId medicationClinicalContextId,
        string recordTitle,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordTitle);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            UPDATE VeteransClaims_MedicationClinicalContexts
            SET RecordTitle = $recordTitle
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue("$recordTitle", recordTitle.Trim());
        command.Parameters.AddWithValue("$id", medicationClinicalContextId.Value);

        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException(
                "Medication clinical context record-title update did not affect exactly one row.");
        }
    }

    public async Task<IReadOnlyList<MedicationClinicalContext>>
        GetMedicationClinicalContextsAsync(
            VeteranId veteranId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT Id, VeteranId, SourceArtifactId, EventDate,
                   SourceStartPage, SourceEndPage, MedicationName,
                   PrescriptionNumber, ContextType, RecordTitle, Summary
            FROM VeteransClaims_MedicationClinicalContexts
            WHERE VeteranId = $veteranId
            ORDER BY EventDate, SourceStartPage, Id;
            """;

        command.Parameters.AddWithValue(
            "$veteranId", veteranId.Value);

        var result = new List<MedicationClinicalContext>();
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
            result.Add(ReadMedicationClinicalContext(reader));

        return result;
    }

    private static async Task InsertMedicationLedgerAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        MedicationLedger medicationLedger,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO VeteransClaims_MedicationLedgers (
                Id, VeteranId, SourceArtifactId, ReportDate,
                SourceStartPage, SourceEndPage, ReportedEntryCount,
                ParsedEntryCount, IsComplete
            )
            VALUES (
                $id, $veteranId, $sourceArtifactId, $reportDate,
                $sourceStartPage, $sourceEndPage, $reportedEntryCount,
                $parsedEntryCount, $isComplete
            );
            """;

        command.Parameters.AddWithValue(
            "$id", medicationLedger.Id.Value);
        command.Parameters.AddWithValue(
            "$veteranId", medicationLedger.VeteranId.Value);
        command.Parameters.AddWithValue(
            "$sourceArtifactId", medicationLedger.SourceArtifactId.Value);
        command.Parameters.AddWithValue(
            "$reportDate", medicationLedger.ReportDate.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue(
            "$sourceStartPage", medicationLedger.SourceStartPage);
        command.Parameters.AddWithValue(
            "$sourceEndPage", medicationLedger.SourceEndPage);
        command.Parameters.AddWithValue(
            "$reportedEntryCount",
            medicationLedger.ReportedEntryCount is null
                ? DBNull.Value
                : medicationLedger.ReportedEntryCount.Value);
        command.Parameters.AddWithValue(
            "$parsedEntryCount", medicationLedger.ParsedEntryCount);
        command.Parameters.AddWithValue(
            "$isComplete", medicationLedger.IsComplete ? 1 : 0);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertMedicationLedgerEntryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        MedicationLedgerEntry entry,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO VeteransClaims_MedicationLedgerEntries (
                Id, MedicationLedgerId, EntryOrdinal,
                SourceStartPage, SourceEndPage, MedicationName,
                Strength, Status, PrescriptionNumber, PrescribedDate,
                LastFilledDate, LastFilledOnText, ExpirationDate,
                RefillsLeft, Directions, Indication, Prescriber,
                Facility, Quantity
            )
            VALUES (
                $id, $medicationLedgerId, $entryOrdinal,
                $sourceStartPage, $sourceEndPage, $medicationName,
                $strength, $status, $prescriptionNumber, $prescribedDate,
                $lastFilledDate, $lastFilledOnText, $expirationDate,
                $refillsLeft, $directions, $indication, $prescriber,
                $facility, $quantity
            );
            """;

        command.Parameters.AddWithValue("$id", entry.Id.Value);
        command.Parameters.AddWithValue(
            "$medicationLedgerId", entry.MedicationLedgerId.Value);
        command.Parameters.AddWithValue("$entryOrdinal", entry.EntryOrdinal);
        command.Parameters.AddWithValue("$sourceStartPage", entry.SourceStartPage);
        command.Parameters.AddWithValue("$sourceEndPage", entry.SourceEndPage);
        command.Parameters.AddWithValue("$medicationName", entry.MedicationName);
        command.Parameters.AddWithValue(
            "$strength", (object?)entry.Strength ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", entry.Status);
        command.Parameters.AddWithValue(
            "$prescriptionNumber",
            (object?)entry.PrescriptionNumber ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$prescribedDate",
            entry.PrescribedDate is null
                ? DBNull.Value
                : entry.PrescribedDate.Value.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue(
            "$lastFilledDate",
            entry.LastFilledDate is null
                ? DBNull.Value
                : entry.LastFilledDate.Value.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue(
            "$lastFilledOnText",
            (object?)entry.LastFilledOnText ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$expirationDate",
            entry.ExpirationDate is null
                ? DBNull.Value
                : entry.ExpirationDate.Value.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue(
            "$refillsLeft",
            entry.RefillsLeft is null
                ? DBNull.Value
                : entry.RefillsLeft.Value);
        command.Parameters.AddWithValue(
            "$directions", (object?)entry.Directions ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$indication", (object?)entry.Indication ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$prescriber", (object?)entry.Prescriber ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$facility", (object?)entry.Facility ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$quantity", (object?)entry.Quantity ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddMedicationHistoryEventAsync(
        MedicationHistoryEvent historyEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(historyEvent);
        ArgumentException.ThrowIfNullOrWhiteSpace(historyEvent.MedicationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(historyEvent.EventType);

        if (historyEvent.SourcePage <= 0)
            throw new ArgumentOutOfRangeException(nameof(historyEvent.SourcePage));

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO VeteransClaims_MedicationHistoryEvents (
                Id, VeteranId, SourceArtifactId, EventDate, SourcePage,
                MedicationName, EventType, Strength, Directions,
                PharmacyIndication, PrescriptionNumber
            )
            VALUES (
                $id, $veteranId, $artifactId, $eventDate, $sourcePage,
                $name, $eventType, $strength, $directions,
                $indication, $rxNumber
            );
            """;

        command.Parameters.AddWithValue("$id", historyEvent.Id.Value);
        command.Parameters.AddWithValue("$veteranId", historyEvent.VeteranId.Value);
        command.Parameters.AddWithValue("$artifactId", historyEvent.SourceArtifactId.Value);
        command.Parameters.AddWithValue("$eventDate", historyEvent.EventDate.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$sourcePage", historyEvent.SourcePage);
        command.Parameters.AddWithValue("$name", historyEvent.MedicationName);
        command.Parameters.AddWithValue("$eventType", historyEvent.EventType);
        command.Parameters.AddWithValue("$strength", (object?)historyEvent.Strength ?? DBNull.Value);
        command.Parameters.AddWithValue("$directions", (object?)historyEvent.Directions ?? DBNull.Value);
        command.Parameters.AddWithValue("$indication", (object?)historyEvent.PharmacyIndication ?? DBNull.Value);
        command.Parameters.AddWithValue("$rxNumber", (object?)historyEvent.PrescriptionNumber ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task<IReadOnlyList<MedicationHistoryEvent>> GetMedicationHistoryEventsAsync(
        VeteranId veteranId,
        CancellationToken cancellationToken = default) =>
        GetMedicationHistoryEventsCoreAsync(veteranId, null, cancellationToken);

    public Task<IReadOnlyList<MedicationHistoryEvent>> GetMedicationHistoryEventsAsync(
        VeteranId veteranId,
        string medicationName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(medicationName);
        return GetMedicationHistoryEventsCoreAsync(
            veteranId, medicationName, cancellationToken);
    }

    private async Task<IReadOnlyList<MedicationHistoryEvent>>
        GetMedicationHistoryEventsCoreAsync(
            VeteranId veteranId,
            string? medicationName,
            CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = medicationName is null
            ? """
              SELECT Id,VeteranId,SourceArtifactId,EventDate,SourcePage,
                     MedicationName,EventType,Strength,Directions,
                     PharmacyIndication,PrescriptionNumber
              FROM VeteransClaims_MedicationHistoryEvents
              WHERE VeteranId=$veteranId
              ORDER BY EventDate, Id;
              """
            : """
              SELECT Id,VeteranId,SourceArtifactId,EventDate,SourcePage,
                     MedicationName,EventType,Strength,Directions,
                     PharmacyIndication,PrescriptionNumber
              FROM VeteransClaims_MedicationHistoryEvents
              WHERE VeteranId=$veteranId
                AND MedicationName=$medicationName COLLATE NOCASE
              ORDER BY EventDate, Id;
              """;

        command.Parameters.AddWithValue("$veteranId", veteranId.Value);
        if (medicationName is not null)
            command.Parameters.AddWithValue("$medicationName", medicationName);

        var result = new List<MedicationHistoryEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(ReadMedicationHistoryEvent(reader));
        return result;
    }

    private static MedicationLedger ReadMedicationLedger(
        SqliteDataReader reader) =>
        new()
        {
            Id = new MedicationLedgerId(reader.GetString(0)),
            VeteranId = new VeteranId(reader.GetString(1)),
            SourceArtifactId = new ArtifactId(reader.GetString(2)),
            ReportDate = DateOnly.Parse(reader.GetString(3)),
            SourceStartPage = reader.GetInt32(4),
            SourceEndPage = reader.GetInt32(5),
            ReportedEntryCount =
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
            ParsedEntryCount = reader.GetInt32(7),
            IsComplete = reader.GetInt32(8) != 0
        };

    private static MedicationLedgerEntry ReadMedicationLedgerEntry(
        SqliteDataReader reader) =>
        new()
        {
            Id = new MedicationLedgerEntryId(reader.GetString(0)),
            MedicationLedgerId =
                new MedicationLedgerId(reader.GetString(1)),
            EntryOrdinal = reader.GetInt32(2),
            SourceStartPage = reader.GetInt32(3),
            SourceEndPage = reader.GetInt32(4),
            MedicationName = reader.GetString(5),
            Strength = reader.IsDBNull(6) ? null : reader.GetString(6),
            Status = reader.GetString(7),
            PrescriptionNumber =
                reader.IsDBNull(8) ? null : reader.GetString(8),
            PrescribedDate =
                reader.IsDBNull(9)
                    ? null
                    : DateOnly.Parse(reader.GetString(9)),
            LastFilledDate =
                reader.IsDBNull(10)
                    ? null
                    : DateOnly.Parse(reader.GetString(10)),
            LastFilledOnText =
                reader.IsDBNull(11) ? null : reader.GetString(11),
            ExpirationDate =
                reader.IsDBNull(12)
                    ? null
                    : DateOnly.Parse(reader.GetString(12)),
            RefillsLeft =
                reader.IsDBNull(13) ? null : reader.GetInt32(13),
            Directions =
                reader.IsDBNull(14) ? null : reader.GetString(14),
            Indication =
                reader.IsDBNull(15) ? null : reader.GetString(15),
            Prescriber =
                reader.IsDBNull(16) ? null : reader.GetString(16),
            Facility =
                reader.IsDBNull(17) ? null : reader.GetString(17),
            Quantity =
                reader.IsDBNull(18) ? null : reader.GetString(18)
        };

    private static MedicationClinicalContext ReadMedicationClinicalContext(
        SqliteDataReader reader) =>
        new()
        {
            Id = new MedicationClinicalContextId(reader.GetString(0)),
            VeteranId = new VeteranId(reader.GetString(1)),
            SourceArtifactId = new ArtifactId(reader.GetString(2)),
            EventDate = DateOnly.Parse(reader.GetString(3)),
            SourceStartPage = reader.GetInt32(4),
            SourceEndPage = reader.GetInt32(5),
            MedicationName = reader.GetString(6),
            PrescriptionNumber = reader.GetString(7),
            ContextType = reader.GetString(8),
            RecordTitle = reader.IsDBNull(9) ? null : reader.GetString(9),
            Summary = reader.GetString(10)
        };

    private static MedicationHistoryEvent ReadMedicationHistoryEvent(
        SqliteDataReader reader) =>
        new()
        {
            Id = new MedicationHistoryEventId(reader.GetString(0)),
            VeteranId = new VeteranId(reader.GetString(1)),
            SourceArtifactId = new ArtifactId(reader.GetString(2)),
            EventDate = DateOnly.Parse(reader.GetString(3)),
            SourcePage = reader.GetInt32(4),
            MedicationName = reader.GetString(5),
            EventType = reader.GetString(6),
            Strength = reader.IsDBNull(7) ? null : reader.GetString(7),
            Directions = reader.IsDBNull(8) ? null : reader.GetString(8),
            PharmacyIndication = reader.IsDBNull(9) ? null : reader.GetString(9),
            PrescriptionNumber = reader.IsDBNull(10) ? null : reader.GetString(10)
        };

    private static MedicationRecord ReadMedicationRecord(
        SqliteDataReader reader)
    {
        return new MedicationRecord
        {
            Id = new MedicationRecordId(reader.GetString(0)),
            VeteranId = new VeteranId(reader.GetString(1)),
            SourceArtifactId = new ArtifactId(reader.GetString(2)),
            RecordDate = DateOnly.Parse(reader.GetString(3)),
            SourcePage = reader.GetInt32(4),
            MedicationName = reader.GetString(5),
            Strength =
                reader.IsDBNull(6) ? null : reader.GetString(6),
            Directions =
                reader.IsDBNull(7) ? null : reader.GetString(7),
            Indication =
                reader.IsDBNull(8) ? null : reader.GetString(8),
            Status = reader.GetString(9),
            SourceDesignation =
                reader.IsDBNull(10) ? null : reader.GetString(10)
        };
    }
}
