using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Models.Identities;
using Microsoft.Data.Sqlite;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

public sealed class SqliteServiceHistoryRepository :
    IServiceHistoryRepository
{
    private readonly string _databasePath;

    public SqliteServiceHistoryRepository(
        string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            databasePath);

        _databasePath = databasePath;
    }

    private SqliteConnection CreateConnection()
    {
        return VeteransClaimsSqliteConnectionFactory
            .Create(_databasePath);
    }

    public Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        return new VeteransClaimsSqliteSchema(
            _databasePath)
            .InitializeAsync(cancellationToken);
    }

    public async Task AddServiceEventAsync(
        ServiceEvent serviceEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceEvent);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VeteransClaims_ServiceEvents (
                Id,
                VeteranId,
                Description
            )
            VALUES (
                $id,
                $veteranId,
                $description
            );
            """;

        command.Parameters.AddWithValue(
            "$id",
            serviceEvent.Id.Value);

        command.Parameters.AddWithValue(
            "$veteranId",
            serviceEvent.VeteranId.Value);

        command.Parameters.AddWithValue(
            "$description",
            serviceEvent.Description);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    public async Task AddExposureAsync(
        Exposure exposure,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exposure);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VeteransClaims_Exposures (
                Id,
                VeteranId,
                ExposureType
            )
            VALUES (
                $id,
                $veteranId,
                $exposureType
            );
            """;

        command.Parameters.AddWithValue(
            "$id",
            exposure.Id.Value);

        command.Parameters.AddWithValue(
            "$veteranId",
            exposure.VeteranId.Value);

        command.Parameters.AddWithValue(
            "$exposureType",
            exposure.ExposureType);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    public async Task<ServiceEvent?> GetServiceEventAsync(
        ServiceEventId serviceEventId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, VeteranId, Description
            FROM VeteransClaims_ServiceEvents
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            serviceEventId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return CreateServiceEvent(reader);
    }

    public async Task<IReadOnlyList<ServiceEvent>>
        GetServiceEventsAsync(
            VeteranId veteranId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, VeteranId, Description
            FROM VeteransClaims_ServiceEvents
            WHERE VeteranId = $veteranId
            ORDER BY Id;
            """;

        command.Parameters.AddWithValue(
            "$veteranId",
            veteranId.Value);

        var serviceEvents = new List<ServiceEvent>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            serviceEvents.Add(CreateServiceEvent(reader));
        }

        return serviceEvents;
    }

    private static ServiceEvent CreateServiceEvent(
        SqliteDataReader reader)
    {
        return new ServiceEvent
        {
            Id =
                new ServiceEventId(
                    reader.GetString(0)),
            VeteranId =
                new VeteranId(
                    reader.GetString(1)),
            Description =
                reader.GetString(2)
        };
    }

    public async Task<Exposure?> GetExposureAsync(
        ExposureId exposureId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, VeteranId, ExposureType
            FROM VeteransClaims_Exposures
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            exposureId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return CreateExposure(reader);
    }

    public async Task<IReadOnlyList<Exposure>>
        GetExposuresAsync(
            VeteranId veteranId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, VeteranId, ExposureType
            FROM VeteransClaims_Exposures
            WHERE VeteranId = $veteranId
            ORDER BY Id;
            """;

        command.Parameters.AddWithValue(
            "$veteranId",
            veteranId.Value);

        var exposures = new List<Exposure>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            exposures.Add(CreateExposure(reader));
        }

        return exposures;
    }

    private static Exposure CreateExposure(
        SqliteDataReader reader)
    {
        return new Exposure
        {
            Id =
                new ExposureId(
                    reader.GetString(0)),
            VeteranId =
                new VeteranId(
                    reader.GetString(1)),
            ExposureType =
                reader.GetString(2)
        };
    }

    public async Task AddServiceEventExposureAsync(
        ServiceEventExposure association,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(association);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction = (SqliteTransaction)
            await connection.BeginTransactionAsync(
                cancellationToken);

        await using var validationCommand =
            connection.CreateCommand();

        validationCommand.Transaction = transaction;
        validationCommand.CommandText =
            """
            SELECT COUNT(*)
            FROM VeteransClaims_ServiceEvents AS serviceEvent
            INNER JOIN VeteransClaims_Exposures AS exposure
                ON exposure.VeteranId = serviceEvent.VeteranId
            WHERE serviceEvent.Id = $serviceEventId
              AND exposure.Id = $exposureId;
            """;

        validationCommand.Parameters.AddWithValue(
            "$serviceEventId",
            association.ServiceEventId.Value);

        validationCommand.Parameters.AddWithValue(
            "$exposureId",
            association.ExposureId.Value);

        var matchingCount =
            Convert.ToInt32(
                await validationCommand.ExecuteScalarAsync(
                    cancellationToken));

        if (matchingCount != 1)
        {
            throw new InvalidOperationException(
                "A service event and exposure must exist " +
                "and belong to the same veteran.");
        }

        await using var insertCommand =
            connection.CreateCommand();

        insertCommand.Transaction = transaction;
        insertCommand.CommandText =
            """
            INSERT INTO
                VeteransClaims_ServiceEventExposures (
                    ServiceEventId,
                    ExposureId
                )
            VALUES (
                $serviceEventId,
                $exposureId
            );
            """;

        insertCommand.Parameters.AddWithValue(
            "$serviceEventId",
            association.ServiceEventId.Value);

        insertCommand.Parameters.AddWithValue(
            "$exposureId",
            association.ExposureId.Value);

        await insertCommand.ExecuteNonQueryAsync(
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }


    public async Task<IReadOnlyList<ExposureId>>
        GetExposureIdsAsync(
            ServiceEventId serviceEventId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ExposureId
            FROM VeteransClaims_ServiceEventExposures
            WHERE ServiceEventId = $serviceEventId
            ORDER BY ExposureId;
            """;

        command.Parameters.AddWithValue(
            "$serviceEventId",
            serviceEventId.Value);

        var exposureIds = new List<ExposureId>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            exposureIds.Add(
                new ExposureId(reader.GetString(0)));
        }

        return exposureIds;
    }

    public async Task<IReadOnlyList<ServiceEventId>>
        GetServiceEventIdsAsync(
            ExposureId exposureId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ServiceEventId
            FROM VeteransClaims_ServiceEventExposures
            WHERE ExposureId = $exposureId
            ORDER BY ServiceEventId;
            """;

        command.Parameters.AddWithValue(
            "$exposureId",
            exposureId.Value);

        var serviceEventIds =
            new List<ServiceEventId>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            serviceEventIds.Add(
                new ServiceEventId(
                    reader.GetString(0)));
        }

        return serviceEventIds;
    }
}
