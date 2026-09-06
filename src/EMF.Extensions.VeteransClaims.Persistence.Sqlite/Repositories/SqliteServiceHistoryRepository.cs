using EMF.Core.Models.Identities;
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


    public async Task AddExposureRegulatoryProvisionAsync(
        ExposureRegulatoryProvision association,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(association);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VeteransClaims_ExposureRegulatoryProvisions (
                ExposureId, RegulatoryProvisionId
            )
            SELECT $exposureId, $regulatoryProvisionId
            WHERE EXISTS (
                SELECT 1 FROM VeteransClaims_Exposures
                WHERE Id = $exposureId
            )
            AND EXISTS (
                SELECT 1 FROM VeteransClaims_RegulatoryProvisions
                WHERE Id = $regulatoryProvisionId
            );
            """;

        command.Parameters.AddWithValue(
            "$exposureId", association.ExposureId.Value);
        command.Parameters.AddWithValue(
            "$regulatoryProvisionId",
            association.RegulatoryProvisionId.Value);

        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException(
                "The exposure and regulatory provision must exist.");
        }
    }

    public async Task<IReadOnlyList<RegulatoryProvisionId>>
        GetRegulatoryProvisionIdsAsync(
            ExposureId exposureId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT RegulatoryProvisionId
            FROM VeteransClaims_ExposureRegulatoryProvisions
            WHERE ExposureId = $exposureId
            ORDER BY RegulatoryProvisionId;
            """;
        command.Parameters.AddWithValue(
            "$exposureId", exposureId.Value);

        var ids = new List<RegulatoryProvisionId>();
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            ids.Add(new RegulatoryProvisionId(reader.GetString(0)));
        return ids;
    }

    public async Task<IReadOnlyList<ExposureId>>
        GetExposureIdsAsync(
            RegulatoryProvisionId regulatoryProvisionId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ExposureId
            FROM VeteransClaims_ExposureRegulatoryProvisions
            WHERE RegulatoryProvisionId = $regulatoryProvisionId
            ORDER BY ExposureId;
            """;
        command.Parameters.AddWithValue(
            "$regulatoryProvisionId", regulatoryProvisionId.Value);

        var ids = new List<ExposureId>();
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            ids.Add(new ExposureId(reader.GetString(0)));
        return ids;
    }


    public async Task AddExposureRequirementAsync(
        ExposureRequirement association,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(association);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VeteransClaims_ExposureRequirements (
                ExposureId, RequirementId
            )
            SELECT $exposureId, $requirementId
            WHERE EXISTS (
                SELECT 1
                FROM VeteransClaims_Exposures
                WHERE Id = $exposureId
            )
            AND EXISTS (
                SELECT 1
                FROM VeteransClaims_Requirements r
                INNER JOIN VeteransClaims_ExposureRegulatoryProvisions p
                    ON p.RegulatoryProvisionId = r.RegulatoryProvisionId
                WHERE r.Id = $requirementId
                  AND p.ExposureId = $exposureId
            );
            """;

        command.Parameters.AddWithValue(
            "$exposureId", association.ExposureId.Value);
        command.Parameters.AddWithValue(
            "$requirementId", association.RequirementId.Value);

        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException(
                "The exposure and requirement must exist, and the " +
                "requirement regulatory provision must be linked to " +
                "the exposure.");
        }
    }

    public async Task<IReadOnlyList<RequirementId>>
        GetRequirementIdsAsync(
            ExposureId exposureId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT RequirementId
            FROM VeteransClaims_ExposureRequirements
            WHERE ExposureId = $exposureId
            ORDER BY RequirementId;
            """;
        command.Parameters.AddWithValue(
            "$exposureId", exposureId.Value);

        var ids = new List<RequirementId>();
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            ids.Add(new RequirementId(reader.GetString(0)));
        return ids;
    }

    public async Task<IReadOnlyList<ExposureId>>
        GetExposureIdsAsync(
            RequirementId requirementId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ExposureId
            FROM VeteransClaims_ExposureRequirements
            WHERE RequirementId = $requirementId
            ORDER BY ExposureId;
            """;
        command.Parameters.AddWithValue(
            "$requirementId", requirementId.Value);

        var ids = new List<ExposureId>();
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            ids.Add(new ExposureId(reader.GetString(0)));
        return ids;
    }

    public async Task AddExposureArtifactAsync(
        ExposureArtifact association,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(association);

        if (association.Role != ExposureTraceabilityRoles.Supporting &&
            association.Role != ExposureTraceabilityRoles.Contradicting &&
            association.Role != ExposureTraceabilityRoles.Qualifying)
        {
            throw new ArgumentException(
                "Exposure artifact role is invalid.",
                nameof(association));
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VeteransClaims_ExposureArtifacts (
                ExposureId, ArtifactId, Role
            )
            SELECT $exposureId, $artifactId, $role
            FROM VeteransClaims_Exposures AS exposure
            INNER JOIN Artifacts AS artifact
                ON artifact.Id = $artifactId
            WHERE exposure.Id = $exposureId;
            """;
        command.Parameters.AddWithValue(
            "$exposureId", association.ExposureId.Value);
        command.Parameters.AddWithValue(
            "$artifactId", association.ArtifactId.Value);
        command.Parameters.AddWithValue(
            "$role", association.Role);

        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException(
                "The exposure and artifact must exist.");
        }
    }

    public Task<IReadOnlyList<ExposureArtifact>>
        GetExposureArtifactsAsync(
            ExposureId exposureId,
            CancellationToken cancellationToken = default)
    {
        return GetExposureArtifactsAsync(
            "ExposureId",
            exposureId.Value,
            cancellationToken);
    }

    public Task<IReadOnlyList<ExposureArtifact>>
        GetExposureArtifactsAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default)
    {
        return GetExposureArtifactsAsync(
            "ArtifactId",
            artifactId.Value,
            cancellationToken);
    }

    private async Task<IReadOnlyList<ExposureArtifact>>
        GetExposureArtifactsAsync(
            string columnName,
            string value,
            CancellationToken cancellationToken)
    {
        if (columnName != "ExposureId" &&
            columnName != "ArtifactId")
        {
            throw new ArgumentOutOfRangeException(nameof(columnName));
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT ExposureId, ArtifactId, Role
            FROM VeteransClaims_ExposureArtifacts
            WHERE {columnName} = $value
            ORDER BY ExposureId, ArtifactId, Role;
            """;
        command.Parameters.AddWithValue("$value", value);

        var results = new List<ExposureArtifact>();
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var role = reader.GetString(2);
            if (role != ExposureTraceabilityRoles.Supporting &&
                role != ExposureTraceabilityRoles.Contradicting &&
                role != ExposureTraceabilityRoles.Qualifying)
            {
                throw new InvalidOperationException(
                    "Stored exposure artifact role is invalid.");
            }

            results.Add(
                new ExposureArtifact
                {
                    ExposureId = new ExposureId(reader.GetString(0)),
                    ArtifactId = new ArtifactId(reader.GetString(1)),
                    Role = role
                });
        }

        return results;
    }


    public async Task AddClaimIssueExposureAsync(
        ClaimIssueExposure association,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(association);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VeteransClaims_ClaimIssueExposures (
                ClaimIssueId,
                ExposureId
            )
            SELECT issue.Id, exposure.Id
            FROM VeteransClaims_ClaimIssues AS issue
            INNER JOIN VeteransClaims_Claims AS claim
                ON claim.Id = issue.ClaimId
            INNER JOIN VeteransClaims_Exposures AS exposure
                ON exposure.Id = $exposureId
            WHERE issue.Id = $claimIssueId
              AND claim.VeteranId = exposure.VeteranId;
            """;

        command.Parameters.AddWithValue(
            "$claimIssueId",
            association.ClaimIssueId.Value);
        command.Parameters.AddWithValue(
            "$exposureId",
            association.ExposureId.Value);

        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException(
                "The claim issue and exposure must exist and belong " +
                "to the same veteran.");
        }
    }

    public async Task<IReadOnlyList<ExposureId>>
        GetExposureIdsAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ExposureId
            FROM VeteransClaims_ClaimIssueExposures
            WHERE ClaimIssueId = $claimIssueId
            ORDER BY ExposureId;
            """;
        command.Parameters.AddWithValue(
            "$claimIssueId", claimIssueId.Value);

        var ids = new List<ExposureId>();
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            ids.Add(new ExposureId(reader.GetString(0)));
        return ids;
    }

    public async Task<IReadOnlyList<ClaimIssueId>>
        GetClaimIssueIdsAsync(
            ExposureId exposureId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ClaimIssueId
            FROM VeteransClaims_ClaimIssueExposures
            WHERE ExposureId = $exposureId
            ORDER BY ClaimIssueId;
            """;
        command.Parameters.AddWithValue(
            "$exposureId", exposureId.Value);

        var ids = new List<ClaimIssueId>();
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            ids.Add(new ClaimIssueId(reader.GetString(0)));
        return ids;
    }
}
