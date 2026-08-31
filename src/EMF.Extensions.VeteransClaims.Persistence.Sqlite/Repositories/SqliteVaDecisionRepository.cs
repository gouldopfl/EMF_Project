using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using Microsoft.Data.Sqlite;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

public sealed class SqliteVaDecisionRepository :
    IVaDecisionRepository
{
    private readonly string _databasePath;

    public SqliteVaDecisionRepository(string databasePath)
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
        var schema =
            new VeteransClaimsSqliteSchema(_databasePath);

        return schema.InitializeAsync(cancellationToken);
    }

    public async Task AddDecisionAsync(
        VaDecision decision,
        IReadOnlyCollection<IssueDecision> issueDecisions,
        IReadOnlyCollection<IssueDecisionSubmission>
            submissionAssociations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(issueDecisions);
        ArgumentNullException.ThrowIfNull(
            submissionAssociations);

        if (issueDecisions.Count == 0)
        {
            throw new ArgumentException(
                "A VA decision must contain at least one issue decision.",
                nameof(issueDecisions));
        }

        var issueDecisionIds =
            issueDecisions
                .Select(item => item.Id)
                .ToHashSet();

        if (issueDecisionIds.Count != issueDecisions.Count)
        {
            throw new ArgumentException(
                "A VA decision cannot contain duplicate issue decisions.",
                nameof(issueDecisions));
        }

        if (issueDecisions.Any(
            item => item.VaDecisionId != decision.Id))
        {
            throw new InvalidOperationException(
                "Every issue decision must reference " +
                "the VA decision being persisted.");
        }

        if (submissionAssociations.Any(
            item => !issueDecisionIds.Contains(
                item.IssueDecisionId)))
        {
            throw new InvalidOperationException(
                "Every submission association must reference " +
                "an issue decision in the transaction.");
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction = (SqliteTransaction)
            await connection.BeginTransactionAsync(
                cancellationToken);

        await ValidateSubmissionAssociationsAsync(
            connection,
            transaction,
            issueDecisions,
            submissionAssociations,
            cancellationToken);

        await InsertDecisionAsync(
            connection,
            transaction,
            decision,
            cancellationToken);

        await InsertIssueDecisionsAsync(
            connection,
            transaction,
            issueDecisions,
            cancellationToken);

        await InsertSubmissionAssociationsAsync(
            connection,
            transaction,
            submissionAssociations,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task AddDecisionDocumentAsync(
        VaDecision decision,
        IReadOnlyCollection<IssueDecision> issueDecisions,
        VaDecisionArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(issueDecisions);
        ArgumentNullException.ThrowIfNull(artifact);

        if (issueDecisions.Count == 0)
            throw new ArgumentException(
                "A VA decision must contain at least one issue decision.",
                nameof(issueDecisions));

        if (issueDecisions.Any(
            item => item.VaDecisionId != decision.Id))
            throw new InvalidOperationException(
                "Every issue decision must reference " +
                "the VA decision being persisted.");

        if (artifact.VaDecisionId != decision.Id)
            throw new InvalidOperationException(
                "The artifact must reference " +
                "the VA decision being persisted.");

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction = (SqliteTransaction)
            await connection.BeginTransactionAsync(
                cancellationToken);

        await InsertDecisionAsync(
            connection,
            transaction,
            decision,
            cancellationToken);

        await InsertIssueDecisionsAsync(
            connection,
            transaction,
            issueDecisions,
            cancellationToken);

        await InsertDecisionArtifactAsync(
            connection,
            transaction,
            artifact,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task
        ValidateSubmissionAssociationsAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            IReadOnlyCollection<IssueDecision> issueDecisions,
            IReadOnlyCollection<IssueDecisionSubmission>
                submissionAssociations,
            CancellationToken cancellationToken)
    {
        var issueDecisionsById =
            issueDecisions.ToDictionary(
                item => item.Id);

        foreach (var association in submissionAssociations)
        {
            var issueDecision =
                issueDecisionsById[
                    association.IssueDecisionId];

            await using var command =
                connection.CreateCommand();

            command.Transaction = transaction;
            command.CommandText =
                """
                SELECT COUNT(*)
                FROM VeteransClaims_SubmissionClaimIssues
                WHERE SubmissionId = $submissionId
                  AND ClaimIssueId = $claimIssueId;
                """;

            command.Parameters.AddWithValue(
                "$submissionId",
                association.SubmissionId.Value);

            command.Parameters.AddWithValue(
                "$claimIssueId",
                issueDecision.ClaimIssueId.Value);

            var matchingCount =
                Convert.ToInt32(
                    await command.ExecuteScalarAsync(
                        cancellationToken));

            if (matchingCount != 1)
            {
                throw new InvalidOperationException(
                    "An associated submission must have " +
                    "presented the decided claim issue.");
            }
        }
    }

    private static async Task InsertDecisionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        VaDecision decision,
        CancellationToken cancellationToken)
    {
        await using var command =
            connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO VeteransClaims_VaDecisions (
                Id,
                DecisionDate
            )
            VALUES (
                $id,
                $decisionDate
            );
            """;

        command.Parameters.AddWithValue(
            "$id",
            decision.Id.Value);

        command.Parameters.AddWithValue(
            "$decisionDate",
            decision.DecisionDate.ToString("O"));

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static async Task InsertIssueDecisionsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyCollection<IssueDecision> issueDecisions,
        CancellationToken cancellationToken)
    {
        foreach (var issueDecision in issueDecisions)
        {
            await using var command =
                connection.CreateCommand();

            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO VeteransClaims_IssueDecisions (
                    Id,
                    VaDecisionId,
                    ClaimIssueId,
                    Outcome
                )
                VALUES (
                    $id,
                    $vaDecisionId,
                    $claimIssueId,
                    $outcome
                );
                """;

            command.Parameters.AddWithValue(
                "$id",
                issueDecision.Id.Value);

            command.Parameters.AddWithValue(
                "$vaDecisionId",
                issueDecision.VaDecisionId.Value);

            command.Parameters.AddWithValue(
                "$claimIssueId",
                issueDecision.ClaimIssueId.Value);

            command.Parameters.AddWithValue(
                "$outcome",
                issueDecision.Outcome);

            await command.ExecuteNonQueryAsync(
                cancellationToken);
        }
    }

    private static async Task
        InsertSubmissionAssociationsAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            IReadOnlyCollection<IssueDecisionSubmission>
                submissionAssociations,
            CancellationToken cancellationToken)
    {
        foreach (var association in submissionAssociations)
        {
            await using var command =
                connection.CreateCommand();

            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO
                    VeteransClaims_IssueDecisionSubmissions (
                        IssueDecisionId,
                        SubmissionId
                    )
                VALUES (
                    $issueDecisionId,
                    $submissionId
                );
                """;

            command.Parameters.AddWithValue(
                "$issueDecisionId",
                association.IssueDecisionId.Value);

            command.Parameters.AddWithValue(
                "$submissionId",
                association.SubmissionId.Value);

            await command.ExecuteNonQueryAsync(
                cancellationToken);
        }
    }


    public async Task AddDecisionArtifactAsync(
        VaDecisionArtifact association,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(association);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction = (SqliteTransaction)
            await connection.BeginTransactionAsync(
                cancellationToken);

        await InsertDecisionArtifactAsync(
            connection,
            transaction,
            association,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task InsertDecisionArtifactAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        VaDecisionArtifact association,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO VeteransClaims_VaDecisionArtifacts (
                VaDecisionId,
                ArtifactId
            )
            VALUES (
                $vaDecisionId,
                $artifactId
            );
            """;

        command.Parameters.AddWithValue(
            "$vaDecisionId",
            association.VaDecisionId.Value);

        command.Parameters.AddWithValue(
            "$artifactId",
            association.ArtifactId.Value);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    public async Task<IReadOnlyList<ArtifactId>>
        GetArtifactIdsAsync(
            VaDecisionId vaDecisionId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ArtifactId
            FROM VeteransClaims_VaDecisionArtifacts
            WHERE VaDecisionId = $vaDecisionId
            ORDER BY ArtifactId;
            """;

        command.Parameters.AddWithValue(
            "$vaDecisionId",
            vaDecisionId.Value);

        var artifactIds = new List<ArtifactId>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            artifactIds.Add(
                new ArtifactId(reader.GetString(0)));
        }

        return artifactIds;
    }

    public async Task<VaDecision?> GetDecisionAsync(
        VaDecisionId vaDecisionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, DecisionDate
            FROM VeteransClaims_VaDecisions
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            vaDecisionId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new VaDecision
        {
            Id =
                new VaDecisionId(reader.GetString(0)),
            DecisionDate =
                DateTimeOffset.Parse(
                    reader.GetString(1))
        };
    }

    public async Task<IReadOnlyList<IssueDecision>>
        GetIssueDecisionsAsync(
            VaDecisionId vaDecisionId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, VaDecisionId, ClaimIssueId, Outcome
            FROM VeteransClaims_IssueDecisions
            WHERE VaDecisionId = $vaDecisionId
            ORDER BY Id;
            """;

        command.Parameters.AddWithValue(
            "$vaDecisionId",
            vaDecisionId.Value);

        var issueDecisions =
            new List<IssueDecision>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            issueDecisions.Add(
                new IssueDecision
                {
                    Id =
                        new IssueDecisionId(
                            reader.GetString(0)),
                    VaDecisionId =
                        new VaDecisionId(
                            reader.GetString(1)),
                    ClaimIssueId =
                        new ClaimIssueId(
                            reader.GetString(2)),
                    Outcome =
                        reader.GetString(3)
                });
        }

        return issueDecisions;
    }

    public async Task<IReadOnlyList<IssueDecision>>
        GetIssueDecisionsAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, VaDecisionId, ClaimIssueId, Outcome
            FROM VeteransClaims_IssueDecisions
            WHERE ClaimIssueId = $claimIssueId
            ORDER BY Id;
            """;

        command.Parameters.AddWithValue(
            "$claimIssueId",
            claimIssueId.Value);

        var issueDecisions =
            new List<IssueDecision>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            issueDecisions.Add(
                new IssueDecision
                {
                    Id =
                        new IssueDecisionId(
                            reader.GetString(0)),
                    VaDecisionId =
                        new VaDecisionId(
                            reader.GetString(1)),
                    ClaimIssueId =
                        new ClaimIssueId(
                            reader.GetString(2)),
                    Outcome =
                        reader.GetString(3)
                });
        }

        return issueDecisions;
    }

    public async Task<IReadOnlyList<SubmissionId>>
        GetSubmissionIdsAsync(
            IssueDecisionId issueDecisionId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT SubmissionId
            FROM VeteransClaims_IssueDecisionSubmissions
            WHERE IssueDecisionId = $issueDecisionId
            ORDER BY SubmissionId;
            """;

        command.Parameters.AddWithValue(
            "$issueDecisionId",
            issueDecisionId.Value);

        var submissionIds =
            new List<SubmissionId>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            submissionIds.Add(
                new SubmissionId(reader.GetString(0)));
        }

        return submissionIds;
    }
}
