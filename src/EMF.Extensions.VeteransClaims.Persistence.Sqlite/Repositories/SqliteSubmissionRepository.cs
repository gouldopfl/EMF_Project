using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using Microsoft.Data.Sqlite;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

public sealed class SqliteSubmissionRepository :
    ISubmissionRepository
{
    private readonly string _databasePath;

    public SqliteSubmissionRepository(string databasePath)
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

    public async Task AddSubmissionAsync(
        Submission submission,
        IReadOnlyCollection<ClaimIssueId> claimIssueIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(claimIssueIds);

        if (claimIssueIds.Count == 0)
        {
            throw new ArgumentException(
                "A submission must present at least one claim issue.",
                nameof(claimIssueIds));
        }

        var distinctClaimIssueIds =
            claimIssueIds.Distinct().ToArray();

        if (distinctClaimIssueIds.Length !=
            claimIssueIds.Count)
        {
            throw new ArgumentException(
                "A submission cannot contain duplicate claim issues.",
                nameof(claimIssueIds));
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction = (SqliteTransaction)
            await connection.BeginTransactionAsync(
                cancellationToken);

        await ValidateClaimIssuesAsync(
            connection,
            transaction,
            submission.ClaimId,
            distinctClaimIssueIds,
            cancellationToken);

        await InsertSubmissionAsync(
            connection,
            transaction,
            submission,
            cancellationToken);

        await InsertClaimIssueAssociationsAsync(
            connection,
            transaction,
            submission,
            distinctClaimIssueIds,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task ValidateClaimIssuesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ClaimId claimId,
        IReadOnlyCollection<ClaimIssueId> claimIssueIds,
        CancellationToken cancellationToken)
    {
        foreach (var claimIssueId in claimIssueIds)
        {
            await using var command =
                connection.CreateCommand();

            command.Transaction = transaction;
            command.CommandText =
                """
                SELECT COUNT(*)
                FROM VeteransClaims_ClaimIssues
                WHERE Id = $claimIssueId
                  AND ClaimId = $claimId;
                """;

            command.Parameters.AddWithValue(
                "$claimIssueId",
                claimIssueId.Value);

            command.Parameters.AddWithValue(
                "$claimId",
                claimId.Value);

            var matchingCount =
                Convert.ToInt32(
                    await command.ExecuteScalarAsync(
                        cancellationToken));

            if (matchingCount != 1)
            {
                throw new InvalidOperationException(
                    "Every submission claim issue must " +
                    "belong to the submission's claim.");
            }
        }
    }

    private static async Task InsertSubmissionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Submission submission,
        CancellationToken cancellationToken)
    {
        await using var command =
            connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO VeteransClaims_Submissions (
                Id,
                ClaimId,
                SubmissionType
            )
            VALUES (
                $id,
                $claimId,
                $submissionType
            );
            """;

        command.Parameters.AddWithValue(
            "$id",
            submission.Id.Value);

        command.Parameters.AddWithValue(
            "$claimId",
            submission.ClaimId.Value);

        command.Parameters.AddWithValue(
            "$submissionType",
            submission.SubmissionType);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static async Task
        InsertClaimIssueAssociationsAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            Submission submission,
            IReadOnlyCollection<ClaimIssueId> claimIssueIds,
            CancellationToken cancellationToken)
    {
        foreach (var claimIssueId in claimIssueIds)
        {
            await using var command =
                connection.CreateCommand();

            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO
                    VeteransClaims_SubmissionClaimIssues (
                        SubmissionId,
                        ClaimIssueId
                    )
                VALUES (
                    $submissionId,
                    $claimIssueId
                );
                """;

            command.Parameters.AddWithValue(
                "$submissionId",
                submission.Id.Value);

            command.Parameters.AddWithValue(
                "$claimIssueId",
                claimIssueId.Value);

            await command.ExecuteNonQueryAsync(
                cancellationToken);
        }
    }

    public async Task<Submission?> GetSubmissionAsync(
        SubmissionId submissionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, ClaimId, SubmissionType
            FROM VeteransClaims_Submissions
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            submissionId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return CreateSubmission(reader);
    }

    private static Submission CreateSubmission(
        SqliteDataReader reader)
    {
        return new Submission
        {
            Id =
                new SubmissionId(reader.GetString(0)),
            ClaimId =
                new ClaimId(reader.GetString(1)),
            SubmissionType =
                reader.GetString(2)
        };
    }

    public async Task<IReadOnlyList<Submission>>
        GetSubmissionsAsync(
            ClaimId claimId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, ClaimId, SubmissionType
            FROM VeteransClaims_Submissions
            WHERE ClaimId = $claimId
            ORDER BY Id;
            """;

        command.Parameters.AddWithValue(
            "$claimId",
            claimId.Value);

        var submissions = new List<Submission>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            submissions.Add(CreateSubmission(reader));
        }

        return submissions;
    }

    public async Task<IReadOnlyList<ClaimIssueId>>
        GetClaimIssueIdsAsync(
            SubmissionId submissionId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ClaimIssueId
            FROM VeteransClaims_SubmissionClaimIssues
            WHERE SubmissionId = $submissionId
            ORDER BY ClaimIssueId;
            """;

        command.Parameters.AddWithValue(
            "$submissionId",
            submissionId.Value);

        var claimIssueIds = new List<ClaimIssueId>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            claimIssueIds.Add(
                new ClaimIssueId(reader.GetString(0)));
        }

        return claimIssueIds;
    }
}
