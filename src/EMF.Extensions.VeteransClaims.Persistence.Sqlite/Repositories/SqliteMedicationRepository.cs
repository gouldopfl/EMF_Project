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
