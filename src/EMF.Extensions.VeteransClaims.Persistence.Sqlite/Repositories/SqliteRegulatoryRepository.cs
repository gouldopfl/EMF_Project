using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Regulatory;
using Microsoft.Data.Sqlite;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

public sealed class SqliteRegulatoryRepository :
    IRegulatoryRepository
{
    private readonly string _databasePath;

    public SqliteRegulatoryRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
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
        return new VeteransClaimsSqliteSchema(_databasePath)
            .InitializeAsync(cancellationToken);
    }
    public async Task AddRegulatoryAuthorityAsync(
        RegulatoryAuthority authority,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authority);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VeteransClaims_RegulatoryAuthorities (
                Id,
                AuthorityType,
                Citation,
                Title
            )
            VALUES (
                $id,
                $authorityType,
                $citation,
                $title
            );
            """;

        command.Parameters.AddWithValue(
            "$id",
            authority.Id.Value);
        command.Parameters.AddWithValue(
            "$authorityType",
            authority.AuthorityType);
        command.Parameters.AddWithValue(
            "$citation",
            authority.Citation);
        command.Parameters.AddWithValue(
            "$title",
            authority.Title);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static RegulatoryAuthority ReadAuthority(
        SqliteDataReader reader)
    {
        return new RegulatoryAuthority
        {
            Id =
                new RegulatoryAuthorityId(
                    reader.GetString(0)),
            AuthorityType = reader.GetString(1),
            Citation = reader.GetString(2),
            Title = reader.GetString(3)
        };
    }

    public async Task<RegulatoryAuthority?>
        GetRegulatoryAuthorityAsync(
            RegulatoryAuthorityId authorityId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, AuthorityType, Citation, Title
            FROM VeteransClaims_RegulatoryAuthorities
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            authorityId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadAuthority(reader);
    }

    public async Task<IReadOnlyList<RegulatoryAuthority>>
        GetRegulatoryAuthoritiesAsync(
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, AuthorityType, Citation, Title
            FROM VeteransClaims_RegulatoryAuthorities
            ORDER BY Id;
            """;

        var authorities =
            new List<RegulatoryAuthority>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            authorities.Add(ReadAuthority(reader));
        }

        return authorities;
    }

    public async Task AddRegulatoryProvisionAsync(
        RegulatoryProvision provision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provision);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VeteransClaims_RegulatoryProvisions (
                Id,
                RegulatoryAuthorityId,
                ProvisionType,
                Citation,
                Version,
                EffectiveFrom,
                EffectiveTo,
                SourceUri,
                SourceHash,
                RetrievedUtc
            )
            VALUES (
                $id,
                $authorityId,
                $provisionType,
                $citation,
                $version,
                $effectiveFrom,
                $effectiveTo,
                $sourceUri,
                $sourceHash,
                $retrievedUtc
            );
            """;

        command.Parameters.AddWithValue(
            "$id",
            provision.Id.Value);
        command.Parameters.AddWithValue(
            "$authorityId",
            provision.RegulatoryAuthorityId.Value);
        command.Parameters.AddWithValue(
            "$provisionType",
            provision.ProvisionType);
        command.Parameters.AddWithValue(
            "$citation",
            provision.Citation);
        command.Parameters.AddWithValue(
            "$version",
            (object?)provision.Version ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$effectiveFrom",
            (object?)provision.EffectiveFrom ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$effectiveTo",
            (object?)provision.EffectiveTo ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$sourceUri",
            (object?)provision.SourceUri ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$sourceHash",
            (object?)provision.SourceHash ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$retrievedUtc",
            (object?)provision.RetrievedUtc ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static RegulatoryProvision ReadProvision(
        SqliteDataReader reader)
    {
        return new RegulatoryProvision
        {
            Id =
                new RegulatoryProvisionId(
                    reader.GetString(0)),
            RegulatoryAuthorityId =
                new RegulatoryAuthorityId(
                    reader.GetString(1)),
            ProvisionType = reader.GetString(2),
            Citation = reader.GetString(3),
            Version = reader.IsDBNull(4)
                ? null
                : reader.GetString(4),
            EffectiveFrom = reader.IsDBNull(5)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(5),
            EffectiveTo = reader.IsDBNull(6)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(6),
            SourceUri = reader.IsDBNull(7)
                ? null
                : reader.GetString(7),
            SourceHash = reader.IsDBNull(8)
                ? null
                : reader.GetString(8),
            RetrievedUtc = reader.IsDBNull(9)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(9)
        };
    }

    public async Task<RegulatoryProvision?>
        GetRegulatoryProvisionAsync(
            RegulatoryProvisionId provisionId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                Id,
                RegulatoryAuthorityId,
                ProvisionType,
                Citation,
                Version,
                EffectiveFrom,
                EffectiveTo,
                SourceUri,
                SourceHash,
                RetrievedUtc
            FROM VeteransClaims_RegulatoryProvisions
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            provisionId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadProvision(reader);
    }

    public async Task<IReadOnlyList<RegulatoryProvision>>
        GetRegulatoryProvisionsAsync(
            RegulatoryAuthorityId authorityId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                Id,
                RegulatoryAuthorityId,
                ProvisionType,
                Citation,
                Version,
                EffectiveFrom,
                EffectiveTo,
                SourceUri,
                SourceHash,
                RetrievedUtc
            FROM VeteransClaims_RegulatoryProvisions
            WHERE RegulatoryAuthorityId = $authorityId
            ORDER BY Id;
            """;

        command.Parameters.AddWithValue(
            "$authorityId",
            authorityId.Value);

        var provisions =
            new List<RegulatoryProvision>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            provisions.Add(ReadProvision(reader));
        }

        return provisions;
    }

    public async Task AddRequirementAsync(
        Requirement requirement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requirement);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VeteransClaims_Requirements (
                Id,
                RegulatoryProvisionId,
                Description
            )
            VALUES (
                $id,
                $provisionId,
                $description
            );
            """;

        command.Parameters.AddWithValue(
            "$id",
            requirement.Id.Value);
        command.Parameters.AddWithValue(
            "$provisionId",
            requirement.RegulatoryProvisionId.Value);
        command.Parameters.AddWithValue(
            "$description",
            requirement.Description);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static Requirement ReadRequirement(
        SqliteDataReader reader)
    {
        return new Requirement
        {
            Id =
                new RequirementId(
                    reader.GetString(0)),
            RegulatoryProvisionId =
                new RegulatoryProvisionId(
                    reader.GetString(1)),
            Description = reader.GetString(2)
        };
    }

    public async Task<Requirement?> GetRequirementAsync(
        RequirementId requirementId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, RegulatoryProvisionId, Description
            FROM VeteransClaims_Requirements
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            requirementId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadRequirement(reader);
    }

    public async Task<IReadOnlyList<Requirement>>
        GetRequirementsAsync(
            RegulatoryProvisionId provisionId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, RegulatoryProvisionId, Description
            FROM VeteransClaims_Requirements
            WHERE RegulatoryProvisionId = $provisionId
            ORDER BY Id;
            """;

        command.Parameters.AddWithValue(
            "$provisionId",
            provisionId.Value);

        var requirements =
            new List<Requirement>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            requirements.Add(ReadRequirement(reader));
        }

        return requirements;
    }

}
