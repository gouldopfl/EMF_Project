using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;
using Microsoft.Data.Sqlite;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

public sealed class SqliteServiceConnectionRepository :
    IServiceConnectionRepository
{
    private readonly string _databasePath;

    public SqliteServiceConnectionRepository(
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

    public async Task AddServiceConnectionTheoryAsync(
        ServiceConnectionTheory theory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(theory);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO
                VeteransClaims_ServiceConnectionTheories (
                    Id,
                    ClaimIssueId,
                    TheoryType
                )
            VALUES (
                $id,
                $claimIssueId,
                $theoryType
            );
            """;

        command.Parameters.AddWithValue(
            "$id",
            theory.Id.Value);

        command.Parameters.AddWithValue(
            "$claimIssueId",
            theory.ClaimIssueId.Value);

        command.Parameters.AddWithValue(
            "$theoryType",
            theory.TheoryType);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }
    public async Task<ServiceConnectionTheory?>
        GetServiceConnectionTheoryAsync(
            ServiceConnectionTheoryId theoryId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, ClaimIssueId, TheoryType
            FROM VeteransClaims_ServiceConnectionTheories
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            theoryId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ServiceConnectionTheory
        {
            Id =
                new ServiceConnectionTheoryId(
                    reader.GetString(0)),
            ClaimIssueId =
                new ClaimIssueId(
                    reader.GetString(1)),
            TheoryType = reader.GetString(2)
        };
    }

    public async Task<IReadOnlyList<ServiceConnectionTheory>>
        GetServiceConnectionTheoriesAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, ClaimIssueId, TheoryType
            FROM VeteransClaims_ServiceConnectionTheories
            WHERE ClaimIssueId = $claimIssueId
            ORDER BY Id;
            """;

        command.Parameters.AddWithValue(
            "$claimIssueId",
            claimIssueId.Value);

        var theories =
            new List<ServiceConnectionTheory>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            theories.Add(
                new ServiceConnectionTheory
                {
                    Id =
                        new ServiceConnectionTheoryId(
                            reader.GetString(0)),
                    ClaimIssueId =
                        new ClaimIssueId(
                            reader.GetString(1)),
                    TheoryType = reader.GetString(2)
                });
        }

        return theories;
    }

    public async Task AddServiceConnectionBasisAsync(
        ServiceConnectionBasis basis,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(basis);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO
                VeteransClaims_ServiceConnectionBases (
                    Id,
                    ClaimIssueId,
                    ServiceConnectionTheoryId
                )
            VALUES (
                $id,
                $claimIssueId,
                $theoryId
            );
            """;

        command.Parameters.AddWithValue(
            "$id",
            basis.Id.Value);

        command.Parameters.AddWithValue(
            "$claimIssueId",
            basis.ClaimIssueId.Value);

        command.Parameters.AddWithValue(
            "$theoryId",
            basis.ServiceConnectionTheoryId.Value);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    public async Task<ServiceConnectionBasis?>
        GetServiceConnectionBasisAsync(
            ServiceConnectionBasisId basisId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                Id,
                ClaimIssueId,
                ServiceConnectionTheoryId
            FROM VeteransClaims_ServiceConnectionBases
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            basisId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ServiceConnectionBasis
        {
            Id =
                new ServiceConnectionBasisId(
                    reader.GetString(0)),
            ClaimIssueId =
                new ClaimIssueId(
                    reader.GetString(1)),
            ServiceConnectionTheoryId =
                new ServiceConnectionTheoryId(
                    reader.GetString(2))
        };
    }

    public async Task<IReadOnlyList<ServiceConnectionBasis>>
        GetServiceConnectionBasesAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                Id,
                ClaimIssueId,
                ServiceConnectionTheoryId
            FROM VeteransClaims_ServiceConnectionBases
            WHERE ClaimIssueId = $claimIssueId
            ORDER BY Id;
            """;

        command.Parameters.AddWithValue(
            "$claimIssueId",
            claimIssueId.Value);

        var bases = new List<ServiceConnectionBasis>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            bases.Add(
                new ServiceConnectionBasis
                {
                    Id =
                        new ServiceConnectionBasisId(
                            reader.GetString(0)),
                    ClaimIssueId =
                        new ClaimIssueId(
                            reader.GetString(1)),
                    ServiceConnectionTheoryId =
                        new ServiceConnectionTheoryId(
                            reader.GetString(2))
                });
        }

        return bases;
    }

    public async Task<IReadOnlyList<ServiceConnectionBasis>>
        GetServiceConnectionBasesAsync(
            ServiceConnectionTheoryId theoryId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                Id,
                ClaimIssueId,
                ServiceConnectionTheoryId
            FROM VeteransClaims_ServiceConnectionBases
            WHERE ServiceConnectionTheoryId = $theoryId
            ORDER BY Id;
            """;

        command.Parameters.AddWithValue(
            "$theoryId",
            theoryId.Value);

        var bases = new List<ServiceConnectionBasis>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            bases.Add(
                new ServiceConnectionBasis
                {
                    Id =
                        new ServiceConnectionBasisId(
                            reader.GetString(0)),
                    ClaimIssueId =
                        new ClaimIssueId(
                            reader.GetString(1)),
                    ServiceConnectionTheoryId =
                        new ServiceConnectionTheoryId(
                            reader.GetString(2))
                });
        }

        return bases;
    }

    public async Task AddBasisClaimedConditionAsync(
        ServiceConnectionBasisClaimedCondition association,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(association);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            (SqliteTransaction)
            await connection.BeginTransactionAsync(
                cancellationToken);

        await using var validationCommand =
            connection.CreateCommand();

        validationCommand.Transaction = transaction;
        validationCommand.CommandText =
            """
            SELECT COUNT(*)
            FROM VeteransClaims_ServiceConnectionBases
                AS basis
            INNER JOIN VeteransClaims_ClaimedConditions
                AS condition
                ON condition.ClaimIssueId =
                    basis.ClaimIssueId
            WHERE basis.Id = $basisId
              AND condition.Id = $claimedConditionId;
            """;

        validationCommand.Parameters.AddWithValue(
            "$basisId",
            association.ServiceConnectionBasisId.Value);

        validationCommand.Parameters.AddWithValue(
            "$claimedConditionId",
            association.ClaimedConditionId.Value);

        var matchingCount =
            Convert.ToInt32(
                await validationCommand
                    .ExecuteScalarAsync(
                        cancellationToken));

        if (matchingCount != 1)
        {
            throw new InvalidOperationException(
                "A service connection basis and claimed " +
                "condition must exist and belong to the " +
                "same claim issue.");
        }

        await using var insertCommand =
            connection.CreateCommand();

        insertCommand.Transaction = transaction;
        insertCommand.CommandText =
            """
            INSERT INTO
                VeteransClaims_BasisClaimedConditions (
                    ServiceConnectionBasisId,
                    ClaimedConditionId
                )
            VALUES (
                $basisId,
                $claimedConditionId
            );
            """;

        insertCommand.Parameters.AddWithValue(
            "$basisId",
            association.ServiceConnectionBasisId.Value);

        insertCommand.Parameters.AddWithValue(
            "$claimedConditionId",
            association.ClaimedConditionId.Value);

        await insertCommand.ExecuteNonQueryAsync(
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);
    }

    public async Task<IReadOnlyList<ClaimedConditionId>>
        GetClaimedConditionIdsAsync(
            ServiceConnectionBasisId basisId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ClaimedConditionId
            FROM VeteransClaims_BasisClaimedConditions
            WHERE ServiceConnectionBasisId = $basisId
            ORDER BY ClaimedConditionId;
            """;

        command.Parameters.AddWithValue(
            "$basisId",
            basisId.Value);

        var claimedConditionIds =
            new List<ClaimedConditionId>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            claimedConditionIds.Add(
                new ClaimedConditionId(
                    reader.GetString(0)));
        }

        return claimedConditionIds;
    }

    public async Task<IReadOnlyList<ServiceConnectionBasisId>>
        GetServiceConnectionBasisIdsAsync(
            ClaimedConditionId claimedConditionId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ServiceConnectionBasisId
            FROM VeteransClaims_BasisClaimedConditions
            WHERE ClaimedConditionId = $claimedConditionId
            ORDER BY ServiceConnectionBasisId;
            """;

        command.Parameters.AddWithValue(
            "$claimedConditionId",
            claimedConditionId.Value);

        var basisIds =
            new List<ServiceConnectionBasisId>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            basisIds.Add(
                new ServiceConnectionBasisId(
                    reader.GetString(0)));
        }

        return basisIds;
    }

    public async Task AddBasisServiceEventAsync(
        ServiceConnectionBasisServiceEvent association,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(association);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            (SqliteTransaction)
            await connection.BeginTransactionAsync(
                cancellationToken);

        await using var validationCommand =
            connection.CreateCommand();

        validationCommand.Transaction = transaction;
        validationCommand.CommandText =
            """
            SELECT COUNT(*)
            FROM VeteransClaims_ServiceConnectionBases
                AS basis
            INNER JOIN VeteransClaims_ClaimIssues
                AS issue
                ON issue.Id = basis.ClaimIssueId
            INNER JOIN VeteransClaims_Claims
                AS claim
                ON claim.Id = issue.ClaimId
            INNER JOIN VeteransClaims_ServiceEvents
                AS serviceEvent
                ON serviceEvent.VeteranId =
                    claim.VeteranId
            WHERE basis.Id = $basisId
              AND serviceEvent.Id = $serviceEventId;
            """;

        validationCommand.Parameters.AddWithValue(
            "$basisId",
            association.ServiceConnectionBasisId.Value);

        validationCommand.Parameters.AddWithValue(
            "$serviceEventId",
            association.ServiceEventId.Value);

        var matchingCount =
            Convert.ToInt32(
                await validationCommand
                    .ExecuteScalarAsync(
                        cancellationToken));

        if (matchingCount != 1)
        {
            throw new InvalidOperationException(
                "A service connection basis and service " +
                "event must exist and belong to the same " +
                "veteran.");
        }

        await using var insertCommand =
            connection.CreateCommand();

        insertCommand.Transaction = transaction;
        insertCommand.CommandText =
            """
            INSERT INTO
                VeteransClaims_BasisServiceEvents (
                    ServiceConnectionBasisId,
                    ServiceEventId
                )
            VALUES (
                $basisId,
                $serviceEventId
            );
            """;

        insertCommand.Parameters.AddWithValue(
            "$basisId",
            association.ServiceConnectionBasisId.Value);

        insertCommand.Parameters.AddWithValue(
            "$serviceEventId",
            association.ServiceEventId.Value);

        await insertCommand.ExecuteNonQueryAsync(
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);
    }

    public async Task<IReadOnlyList<ServiceEventId>>
        GetServiceEventIdsAsync(
            ServiceConnectionBasisId basisId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ServiceEventId
            FROM VeteransClaims_BasisServiceEvents
            WHERE ServiceConnectionBasisId = $basisId
            ORDER BY ServiceEventId;
            """;

        command.Parameters.AddWithValue(
            "$basisId",
            basisId.Value);

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

    public async Task<IReadOnlyList<ServiceConnectionBasisId>>
        GetServiceConnectionBasisIdsAsync(
            ServiceEventId serviceEventId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ServiceConnectionBasisId
            FROM VeteransClaims_BasisServiceEvents
            WHERE ServiceEventId = $serviceEventId
            ORDER BY ServiceConnectionBasisId;
            """;

        command.Parameters.AddWithValue(
            "$serviceEventId",
            serviceEventId.Value);

        var basisIds =
            new List<ServiceConnectionBasisId>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            basisIds.Add(
                new ServiceConnectionBasisId(
                    reader.GetString(0)));
        }

        return basisIds;
    }


    public async Task AddBasisExposureAsync(
        ServiceConnectionBasisExposure association,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(association);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            (SqliteTransaction)
            await connection.BeginTransactionAsync(
                cancellationToken);

        await using var validationCommand =
            connection.CreateCommand();

        validationCommand.Transaction = transaction;
        validationCommand.CommandText =
            """
            SELECT COUNT(*)
            FROM VeteransClaims_ServiceConnectionBases
                AS basis
            INNER JOIN VeteransClaims_ClaimIssues
                AS issue
                ON issue.Id = basis.ClaimIssueId
            INNER JOIN VeteransClaims_Claims
                AS claim
                ON claim.Id = issue.ClaimId
            INNER JOIN VeteransClaims_Exposures
                AS exposure
                ON exposure.VeteranId =
                    claim.VeteranId
            WHERE basis.Id = $basisId
              AND exposure.Id = $exposureId;
            """;

        validationCommand.Parameters.AddWithValue(
            "$basisId",
            association.ServiceConnectionBasisId.Value);

        validationCommand.Parameters.AddWithValue(
            "$exposureId",
            association.ExposureId.Value);

        var matchingCount =
            Convert.ToInt32(
                await validationCommand
                    .ExecuteScalarAsync(
                        cancellationToken));

        if (matchingCount != 1)
        {
            throw new InvalidOperationException(
                "A service connection basis and exposure " +
                "must exist and belong to the same veteran.");
        }

        await using var insertCommand =
            connection.CreateCommand();

        insertCommand.Transaction = transaction;
        insertCommand.CommandText =
            """
            INSERT INTO
                VeteransClaims_BasisExposures (
                    ServiceConnectionBasisId,
                    ExposureId
                )
            VALUES (
                $basisId,
                $exposureId
            );
            """;

        insertCommand.Parameters.AddWithValue(
            "$basisId",
            association.ServiceConnectionBasisId.Value);

        insertCommand.Parameters.AddWithValue(
            "$exposureId",
            association.ExposureId.Value);

        await insertCommand.ExecuteNonQueryAsync(
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);
    }

    public async Task<IReadOnlyList<ExposureId>>
        GetExposureIdsAsync(
            ServiceConnectionBasisId basisId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ExposureId
            FROM VeteransClaims_BasisExposures
            WHERE ServiceConnectionBasisId = $basisId
            ORDER BY ExposureId;
            """;

        command.Parameters.AddWithValue(
            "$basisId",
            basisId.Value);

        var exposureIds =
            new List<ExposureId>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            exposureIds.Add(
                new ExposureId(
                    reader.GetString(0)));
        }

        return exposureIds;
    }

    public async Task<IReadOnlyList<ServiceConnectionBasisId>>
        GetServiceConnectionBasisIdsAsync(
            ExposureId exposureId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ServiceConnectionBasisId
            FROM VeteransClaims_BasisExposures
            WHERE ExposureId = $exposureId
            ORDER BY ServiceConnectionBasisId;
            """;

        command.Parameters.AddWithValue(
            "$exposureId",
            exposureId.Value);

        var basisIds =
            new List<ServiceConnectionBasisId>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            basisIds.Add(
                new ServiceConnectionBasisId(
                    reader.GetString(0)));
        }

        return basisIds;
    }

    public async Task AddBasisServiceConnectedConditionAsync(
        ServiceConnectionBasisServiceConnectedCondition association,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(association);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            (SqliteTransaction)
            await connection.BeginTransactionAsync(
                cancellationToken);

        await using var validationCommand =
            connection.CreateCommand();

        validationCommand.Transaction = transaction;
        validationCommand.CommandText =
            """
            SELECT COUNT(*)
            FROM VeteransClaims_ServiceConnectionBases AS basis
            INNER JOIN VeteransClaims_ClaimIssues AS issue
                ON issue.Id = basis.ClaimIssueId
            INNER JOIN VeteransClaims_Claims AS claim
                ON claim.Id = issue.ClaimId
            INNER JOIN VeteransClaims_VeteranMedicalConditions
                AS condition
                ON condition.VeteranId = claim.VeteranId
            WHERE basis.Id = $basisId
              AND condition.MedicalConditionId =
                  $serviceConnectedConditionId;
            """;

        validationCommand.Parameters.AddWithValue(
            "$basisId",
            association.ServiceConnectionBasisId.Value);

        validationCommand.Parameters.AddWithValue(
            "$serviceConnectedConditionId",
            association.ServiceConnectedConditionId.Value);

        var matchingCount =
            Convert.ToInt32(
                await validationCommand.ExecuteScalarAsync(
                    cancellationToken));

        if (matchingCount != 1)
        {
            throw new InvalidOperationException(
                "A service connection basis and service-" +
                "connected condition must exist and belong " +
                "to the same veteran.");
        }

        await using var insertCommand =
            connection.CreateCommand();

        insertCommand.Transaction = transaction;
        insertCommand.CommandText =
            """
            INSERT INTO
                VeteransClaims_BasisServiceConnectedConditions (
                    ServiceConnectionBasisId,
                    ServiceConnectedConditionId
                )
            VALUES ($basisId, $serviceConnectedConditionId);
            """;

        insertCommand.Parameters.AddWithValue(
            "$basisId",
            association.ServiceConnectionBasisId.Value);

        insertCommand.Parameters.AddWithValue(
            "$serviceConnectedConditionId",
            association.ServiceConnectedConditionId.Value);

        await insertCommand.ExecuteNonQueryAsync(
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);
    }

    public async Task<IReadOnlyList<MedicalConditionId>>
        GetServiceConnectedConditionIdsAsync(
            ServiceConnectionBasisId basisId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ServiceConnectedConditionId
            FROM VeteransClaims_BasisServiceConnectedConditions
            WHERE ServiceConnectionBasisId = $basisId
            ORDER BY ServiceConnectedConditionId;
            """;

        command.Parameters.AddWithValue(
            "$basisId",
            basisId.Value);

        var conditionIds =
            new List<MedicalConditionId>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            conditionIds.Add(
                new MedicalConditionId(
                    reader.GetString(0)));
        }

        return conditionIds;
    }

    public async Task<IReadOnlyList<ServiceConnectionBasisId>>
        GetServiceConnectedConditionBasisIdsAsync(
            MedicalConditionId serviceConnectedConditionId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ServiceConnectionBasisId
            FROM VeteransClaims_BasisServiceConnectedConditions
            WHERE ServiceConnectedConditionId =
                $serviceConnectedConditionId
            ORDER BY ServiceConnectionBasisId;
            """;

        command.Parameters.AddWithValue(
            "$serviceConnectedConditionId",
            serviceConnectedConditionId.Value);

        var basisIds =
            new List<ServiceConnectionBasisId>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            basisIds.Add(
                new ServiceConnectionBasisId(
                    reader.GetString(0)));
        }

        return basisIds;
    }

    public async Task AddBasisPreexistingConditionAsync(
        ServiceConnectionBasisPreexistingCondition association,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(association);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            (SqliteTransaction)
            await connection.BeginTransactionAsync(
                cancellationToken);

        await using var validationCommand =
            connection.CreateCommand();

        validationCommand.Transaction = transaction;
        validationCommand.CommandText =
            """
            SELECT COUNT(*)
            FROM VeteransClaims_ServiceConnectionBases AS basis
            INNER JOIN VeteransClaims_ClaimIssues AS issue
                ON issue.Id = basis.ClaimIssueId
            INNER JOIN VeteransClaims_Claims AS claim
                ON claim.Id = issue.ClaimId
            INNER JOIN VeteransClaims_VeteranMedicalConditions
                AS condition
                ON condition.VeteranId = claim.VeteranId
            WHERE basis.Id = $basisId
              AND condition.MedicalConditionId =
                  $preexistingConditionId;
            """;

        validationCommand.Parameters.AddWithValue(
            "$basisId",
            association.ServiceConnectionBasisId.Value);

        validationCommand.Parameters.AddWithValue(
            "$preexistingConditionId",
            association.PreexistingConditionId.Value);

        var matchingCount =
            Convert.ToInt32(
                await validationCommand.ExecuteScalarAsync(
                    cancellationToken));

        if (matchingCount != 1)
        {
            throw new InvalidOperationException(
                "A service connection basis and service-" +
                "connected condition must exist and belong " +
                "to the same veteran.");
        }

        await using var insertCommand =
            connection.CreateCommand();

        insertCommand.Transaction = transaction;
        insertCommand.CommandText =
            """
            INSERT INTO
                VeteransClaims_BasisPreexistingConditions (
                    ServiceConnectionBasisId,
                    PreexistingConditionId
                )
            VALUES ($basisId, $preexistingConditionId);
            """;

        insertCommand.Parameters.AddWithValue(
            "$basisId",
            association.ServiceConnectionBasisId.Value);

        insertCommand.Parameters.AddWithValue(
            "$preexistingConditionId",
            association.PreexistingConditionId.Value);

        await insertCommand.ExecuteNonQueryAsync(
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);
    }

    public async Task<IReadOnlyList<MedicalConditionId>>
        GetPreexistingConditionIdsAsync(
            ServiceConnectionBasisId basisId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT PreexistingConditionId
            FROM VeteransClaims_BasisPreexistingConditions
            WHERE ServiceConnectionBasisId = $basisId
            ORDER BY PreexistingConditionId;
            """;

        command.Parameters.AddWithValue(
            "$basisId",
            basisId.Value);

        var conditionIds =
            new List<MedicalConditionId>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            conditionIds.Add(
                new MedicalConditionId(
                    reader.GetString(0)));
        }

        return conditionIds;
    }

    public async Task<IReadOnlyList<ServiceConnectionBasisId>>
        GetPreexistingConditionBasisIdsAsync(
            MedicalConditionId preexistingConditionId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ServiceConnectionBasisId
            FROM VeteransClaims_BasisPreexistingConditions
            WHERE PreexistingConditionId =
                $preexistingConditionId
            ORDER BY ServiceConnectionBasisId;
            """;

        command.Parameters.AddWithValue(
            "$preexistingConditionId",
            preexistingConditionId.Value);

        var basisIds =
            new List<ServiceConnectionBasisId>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            basisIds.Add(
                new ServiceConnectionBasisId(
                    reader.GetString(0)));
        }

        return basisIds;
    }

}
