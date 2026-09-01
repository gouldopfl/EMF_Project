using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using Microsoft.Data.Sqlite;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

public sealed class SqliteDisabilityEvaluationRepository :
    IDisabilityEvaluationRepository
{
    private readonly string _databasePath;

    public SqliteDisabilityEvaluationRepository(
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
        var schema =
            new VeteransClaimsSqliteSchema(_databasePath);

        return schema.InitializeAsync(cancellationToken);
    }

    public async Task AddEvaluationsAsync(
        IssueDecisionId issueDecisionId,
        IReadOnlyCollection<DisabilityEvaluation>
            evaluations,
        IReadOnlyCollection<EffectiveDate>
            effectiveDates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evaluations);
        ArgumentNullException.ThrowIfNull(effectiveDates);

        if (evaluations.Count == 0)
        {
            throw new ArgumentException(
                "At least one disability evaluation is required.",
                nameof(evaluations));
        }

        var evaluationIds =
            evaluations
                .Select(item => item.Id)
                .ToHashSet();

        if (evaluationIds.Count != evaluations.Count)
        {
            throw new ArgumentException(
                "Disability evaluation IDs must be unique.",
                nameof(evaluations));
        }

        if (evaluations.Any(
            item =>
                item.IssueDecisionId != issueDecisionId))
        {
            throw new InvalidOperationException(
                "Every evaluation must reference the " +
                "specified issue decision.");
        }

        var effectiveDateIds =
            effectiveDates
                .Select(item => item.Id)
                .ToHashSet();

        var datedEvaluationIds =
            effectiveDates
                .Select(
                    item =>
                        item.DisabilityEvaluationId)
                .ToHashSet();

        if (effectiveDateIds.Count != effectiveDates.Count ||
            datedEvaluationIds.Count != effectiveDates.Count)
        {
            throw new ArgumentException(
                "Effective dates must have unique identities " +
                "and one date per evaluation.",
                nameof(effectiveDates));
        }

        if (effectiveDates.Any(
            item => !evaluationIds.Contains(
                item.DisabilityEvaluationId)))
        {
            throw new InvalidOperationException(
                "Every effective date must reference an " +
                "evaluation in the transaction.");
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction = (SqliteTransaction)
            await connection.BeginTransactionAsync(
                cancellationToken);

        await InsertEvaluationsAsync(
            connection,
            transaction,
            evaluations,
            cancellationToken);

        await InsertEffectiveDatesAsync(
            connection,
            transaction,
            effectiveDates,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task InsertEvaluationsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyCollection<DisabilityEvaluation>
            evaluations,
        CancellationToken cancellationToken)
    {
        foreach (var evaluation in evaluations)
        {
            await using var command =
                connection.CreateCommand();

            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO
                    VeteransClaims_DisabilityEvaluations (
                        Id,
                        IssueDecisionId,
                        Evaluation
                    )
                VALUES (
                    $id,
                    $issueDecisionId,
                    $evaluation
                );
                """;

            command.Parameters.AddWithValue(
                "$id",
                evaluation.Id.Value);

            command.Parameters.AddWithValue(
                "$issueDecisionId",
                evaluation.IssueDecisionId.Value);

            command.Parameters.AddWithValue(
                "$evaluation",
                evaluation.Evaluation);

            await command.ExecuteNonQueryAsync(
                cancellationToken);
        }
    }

    private static async Task InsertEffectiveDatesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyCollection<EffectiveDate>
            effectiveDates,
        CancellationToken cancellationToken)
    {
        foreach (var effectiveDate in effectiveDates)
        {
            await using var command =
                connection.CreateCommand();

            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO VeteransClaims_EffectiveDates (
                    Id,
                    DisabilityEvaluationId,
                    EffectiveDate
                )
                VALUES (
                    $id,
                    $disabilityEvaluationId,
                    $effectiveDate
                );
                """;

            command.Parameters.AddWithValue(
                "$id",
                effectiveDate.Id.Value);

            command.Parameters.AddWithValue(
                "$disabilityEvaluationId",
                effectiveDate.DisabilityEvaluationId.Value);

            command.Parameters.AddWithValue(
                "$effectiveDate",
                effectiveDate.Date.ToString("O"));

            await command.ExecuteNonQueryAsync(
                cancellationToken);
        }
    }

    public async Task<IReadOnlyList<DisabilityEvaluation>>
        GetEvaluationsAsync(
            IssueDecisionId issueDecisionId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, IssueDecisionId, Evaluation
            FROM VeteransClaims_DisabilityEvaluations
            WHERE IssueDecisionId = $issueDecisionId
            ORDER BY Id;
            """;

        command.Parameters.AddWithValue(
            "$issueDecisionId",
            issueDecisionId.Value);

        var evaluations =
            new List<DisabilityEvaluation>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            evaluations.Add(
                new DisabilityEvaluation
                {
                    Id =
                        new DisabilityEvaluationId(
                            reader.GetString(0)),
                    IssueDecisionId =
                        new IssueDecisionId(
                            reader.GetString(1)),
                    Evaluation =
                        reader.GetString(2)
                });
        }

        return evaluations;
    }

    public async Task<DisabilityEvaluation?>
        GetCurrentEvaluationAsync(
            IssueDecisionId issueDecisionId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT e.Id, e.IssueDecisionId, e.Evaluation
            FROM VeteransClaims_DisabilityEvaluations e
            JOIN VeteransClaims_EffectiveDates d
                ON d.DisabilityEvaluationId = e.Id
            WHERE e.IssueDecisionId = $issueDecisionId
            ORDER BY d.EffectiveDate DESC
            LIMIT 1;
            """;

        command.Parameters.AddWithValue(
            "$issueDecisionId",
            issueDecisionId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new DisabilityEvaluation
        {
            Id =
                new DisabilityEvaluationId(
                    reader.GetString(0)),
            IssueDecisionId =
                new IssueDecisionId(
                    reader.GetString(1)),
            Evaluation =
                reader.GetString(2)
        };
    }

    public async Task<EffectiveDate?>
        GetEffectiveDateAsync(
            DisabilityEvaluationId disabilityEvaluationId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, DisabilityEvaluationId, EffectiveDate
            FROM VeteransClaims_EffectiveDates
            WHERE DisabilityEvaluationId =
                $disabilityEvaluationId;
            """;

        command.Parameters.AddWithValue(
            "$disabilityEvaluationId",
            disabilityEvaluationId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new EffectiveDate
        {
            Id =
                new EffectiveDateId(reader.GetString(0)),
            DisabilityEvaluationId =
                new DisabilityEvaluationId(
                    reader.GetString(1)),
            Date =
                DateOnly.Parse(reader.GetString(2))
        };
    }
}
