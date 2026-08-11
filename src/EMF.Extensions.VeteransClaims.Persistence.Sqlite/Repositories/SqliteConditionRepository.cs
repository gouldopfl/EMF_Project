using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Conditions;
using EMF.Extensions.VeteransClaims.Models.Identities;
using Microsoft.Data.Sqlite;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

public sealed class SqliteConditionRepository :
    IConditionRepository
{
    private readonly string _databasePath;

    public SqliteConditionRepository(
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

    public async Task AddClaimedConditionAsync(
        ClaimedCondition claimedCondition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            claimedCondition);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VeteransClaims_ClaimedConditions (
                Id,
                ClaimIssueId,
                Name
            )
            VALUES (
                $id,
                $claimIssueId,
                $name
            );
            """;

        command.Parameters.AddWithValue(
            "$id",
            claimedCondition.Id.Value);

        command.Parameters.AddWithValue(
            "$claimIssueId",
            claimedCondition.ClaimIssueId.Value);

        command.Parameters.AddWithValue(
            "$name",
            claimedCondition.Name);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }
    public async Task<ClaimedCondition?>
        GetClaimedConditionAsync(
            ClaimedConditionId claimedConditionId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, ClaimIssueId, Name
            FROM VeteransClaims_ClaimedConditions
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            claimedConditionId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ClaimedCondition
        {
            Id =
                new ClaimedConditionId(
                    reader.GetString(0)),
            ClaimIssueId =
                new ClaimIssueId(
                    reader.GetString(1)),
            Name = reader.GetString(2)
        };
    }


    public async Task<IReadOnlyList<ClaimedCondition>>
        GetClaimedConditionsAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, ClaimIssueId, Name
            FROM VeteransClaims_ClaimedConditions
            WHERE ClaimIssueId = $claimIssueId
            ORDER BY Id;
            """;

        command.Parameters.AddWithValue(
            "$claimIssueId",
            claimIssueId.Value);

        var conditions = new List<ClaimedCondition>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            conditions.Add(
                new ClaimedCondition
                {
                    Id =
                        new ClaimedConditionId(
                            reader.GetString(0)),
                    ClaimIssueId =
                        new ClaimIssueId(
                            reader.GetString(1)),
                    Name = reader.GetString(2)
                });
        }

        return conditions;
    }

    public async Task AddMedicalConditionAsync(
        MedicalCondition medicalCondition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            medicalCondition);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VeteransClaims_MedicalConditions (
                Id,
                Name
            )
            VALUES (
                $id,
                $name
            );
            """;

        command.Parameters.AddWithValue(
            "$id",
            medicalCondition.Id.Value);

        command.Parameters.AddWithValue(
            "$name",
            medicalCondition.Name);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    public async Task<MedicalCondition?>
        GetMedicalConditionAsync(
            MedicalConditionId medicalConditionId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, Name
            FROM VeteransClaims_MedicalConditions
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            medicalConditionId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new MedicalCondition
        {
            Id =
                new MedicalConditionId(
                    reader.GetString(0)),
            Name = reader.GetString(1)
        };
    }

    public async Task AddVeteranMedicalConditionAsync(
        VeteranMedicalCondition association,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(association);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO
                VeteransClaims_VeteranMedicalConditions (
                    VeteranId,
                    MedicalConditionId
                )
            VALUES (
                $veteranId,
                $medicalConditionId
            );
            """;

        command.Parameters.AddWithValue(
            "$veteranId",
            association.VeteranId.Value);

        command.Parameters.AddWithValue(
            "$medicalConditionId",
            association.MedicalConditionId.Value);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    public async Task<IReadOnlyList<MedicalConditionId>>
        GetMedicalConditionIdsAsync(
            VeteranId veteranId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT MedicalConditionId
            FROM VeteransClaims_VeteranMedicalConditions
            WHERE VeteranId = $veteranId
            ORDER BY MedicalConditionId;
            """;

        command.Parameters.AddWithValue(
            "$veteranId",
            veteranId.Value);

        var medicalConditionIds =
            new List<MedicalConditionId>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            medicalConditionIds.Add(
                new MedicalConditionId(
                    reader.GetString(0)));
        }

        return medicalConditionIds;
    }

    public async Task<IReadOnlyList<VeteranId>>
        GetVeteranIdsAsync(
            MedicalConditionId medicalConditionId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT VeteranId
            FROM VeteransClaims_VeteranMedicalConditions
            WHERE MedicalConditionId = $medicalConditionId
            ORDER BY VeteranId;
            """;

        command.Parameters.AddWithValue(
            "$medicalConditionId",
            medicalConditionId.Value);

        var veteranIds = new List<VeteranId>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            veteranIds.Add(
                new VeteranId(
                    reader.GetString(0)));
        }

        return veteranIds;
    }

    public async Task
        AddClaimedConditionMedicalConditionAsync(
            ClaimedConditionMedicalCondition association,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(association);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO
                VeteransClaims_ClaimedConditionMedicalConditions (
                    ClaimedConditionId,
                    MedicalConditionId
                )
            VALUES (
                $claimedConditionId,
                $medicalConditionId
            );
            """;

        command.Parameters.AddWithValue(
            "$claimedConditionId",
            association.ClaimedConditionId.Value);

        command.Parameters.AddWithValue(
            "$medicalConditionId",
            association.MedicalConditionId.Value);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    public async Task<IReadOnlyList<MedicalConditionId>>
        GetMedicalConditionIdsAsync(
            ClaimedConditionId claimedConditionId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT MedicalConditionId
            FROM
                VeteransClaims_ClaimedConditionMedicalConditions
            WHERE ClaimedConditionId = $claimedConditionId
            ORDER BY MedicalConditionId;
            """;

        command.Parameters.AddWithValue(
            "$claimedConditionId",
            claimedConditionId.Value);

        var medicalConditionIds =
            new List<MedicalConditionId>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            medicalConditionIds.Add(
                new MedicalConditionId(
                    reader.GetString(0)));
        }

        return medicalConditionIds;
    }

    public async Task<IReadOnlyList<ClaimedConditionId>>
        GetClaimedConditionIdsAsync(
            MedicalConditionId medicalConditionId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ClaimedConditionId
            FROM
                VeteransClaims_ClaimedConditionMedicalConditions
            WHERE MedicalConditionId = $medicalConditionId
            ORDER BY ClaimedConditionId;
            """;

        command.Parameters.AddWithValue(
            "$medicalConditionId",
            medicalConditionId.Value);

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

}
