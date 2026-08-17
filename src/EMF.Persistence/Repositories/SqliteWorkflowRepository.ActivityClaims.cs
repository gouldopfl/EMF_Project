using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;

namespace EMF.Persistence.Repositories;

public sealed partial class SqliteWorkflowRepository
{
    public async Task<bool> TryClaimActivityAsync(
        WorkflowId workflowId,
        string activityId,
        string claimId,
        DateTimeOffset claimedUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(claimId);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO WorkflowActivityClaims
                (WorkflowId, ActivityId, ClaimId, Status, ClaimedUtc)
            VALUES
                ($workflowId, $activityId, $claimId, 'Claimed', $claimedUtc)
            ON CONFLICT (WorkflowId, ActivityId) DO NOTHING;
            """;

        command.Parameters.AddWithValue(
            "$workflowId", workflowId.Value);
        command.Parameters.AddWithValue(
            "$activityId", activityId);
        command.Parameters.AddWithValue(
            "$claimId", claimId);
        command.Parameters.AddWithValue(
            "$claimedUtc", claimedUtc.ToString("O"));

        return await command.ExecuteNonQueryAsync(
            cancellationToken) == 1;
    }

    public async Task CompleteActivityClaimAsync(
        WorkflowId workflowId,
        string activityId,
        string claimId,
        DateTimeOffset completedUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(claimId);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            UPDATE WorkflowActivityClaims
            SET Status = 'Completed',
                CompletedUtc = $completedUtc
            WHERE WorkflowId = $workflowId
              AND ActivityId = $activityId
              AND ClaimId = $claimId
              AND Status = 'Claimed';
            """;

        command.Parameters.AddWithValue(
            "$completedUtc", completedUtc.ToString("O"));
        command.Parameters.AddWithValue(
            "$workflowId", workflowId.Value);
        command.Parameters.AddWithValue(
            "$activityId", activityId);
        command.Parameters.AddWithValue(
            "$claimId", claimId);

        var affectedRows =
            await command.ExecuteNonQueryAsync(
                cancellationToken);

        if (affectedRows != 1)
        {
            throw CreateClaimException(
                workflowId,
                activityId,
                claimId,
                "could not be completed");
        }
    }

    public async Task ReleaseActivityClaimAsync(
        WorkflowId workflowId,
        string activityId,
        string claimId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(claimId);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            DELETE FROM WorkflowActivityClaims
            WHERE WorkflowId = $workflowId
              AND ActivityId = $activityId
              AND ClaimId = $claimId
              AND Status = 'Claimed';
            """;

        command.Parameters.AddWithValue(
            "$workflowId", workflowId.Value);
        command.Parameters.AddWithValue(
            "$activityId", activityId);
        command.Parameters.AddWithValue(
            "$claimId", claimId);

        var affectedRows =
            await command.ExecuteNonQueryAsync(
                cancellationToken);

        if (affectedRows != 1)
        {
            throw CreateClaimException(
                workflowId,
                activityId,
                claimId,
                "could not be released");
        }
    }

    private static WorkflowActivityClaimException
        CreateClaimException(
            WorkflowId workflowId,
            string activityId,
            string claimId,
            string failure)
    {
        return new WorkflowActivityClaimException(
            workflowId,
            activityId,
            claimId,
            $"Activity claim '{claimId}' for workflow " +
            $"'{workflowId}' activity '{activityId}' {failure}.");
    }
}
