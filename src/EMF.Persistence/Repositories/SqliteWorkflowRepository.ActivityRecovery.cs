using EMF.Core.Models.Identities;

namespace EMF.Persistence.Repositories;

public sealed partial class SqliteWorkflowRepository
{
    public async Task<bool> TryReclaimActivityAsync(
        WorkflowId workflowId,
        string activityId,
        string newClaimId,
        DateTimeOffset reclaimedUtc,
        DateTimeOffset abandonedBeforeUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newClaimId);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            UPDATE WorkflowActivityClaims
            SET ClaimId = $newClaimId,
                ClaimedUtc = $reclaimedUtc,
                CompletedUtc = NULL
            WHERE WorkflowId = $workflowId
              AND ActivityId = $activityId
              AND Status = 'Claimed'
              AND julianday(ClaimedUtc) <=
                  julianday($abandonedBeforeUtc);
            """;

        command.Parameters.AddWithValue(
            "$newClaimId", newClaimId);
        command.Parameters.AddWithValue(
            "$reclaimedUtc", reclaimedUtc.ToString("O"));
        command.Parameters.AddWithValue(
            "$workflowId", workflowId.Value);
        command.Parameters.AddWithValue(
            "$activityId", activityId);
        command.Parameters.AddWithValue(
            "$abandonedBeforeUtc",
            abandonedBeforeUtc.ToString("O"));

        return await command.ExecuteNonQueryAsync(
            cancellationToken) == 1;
    }
}
