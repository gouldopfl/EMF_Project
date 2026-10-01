using EMF.Core.Models.Workflow;
using EMF.Orchestration.Contracts;

namespace EMF.Orchestration.Services;

public sealed class WorkflowRecoveryPolicy : IWorkflowRecoveryPolicy
{
    public Task<RecoveryDecision> EvaluateAsync(
        WorkflowExecutionRecord execution,
        WorkflowDefinition definition,
        IReadOnlyList<WorkflowCheckpoint> checkpoints,
        IReadOnlyList<WorkflowOperationRecord> operations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(checkpoints);
        ArgumentNullException.ThrowIfNull(operations);

        if (!string.Equals(
                execution.DefinitionId,
                definition.Id,
                StringComparison.Ordinal) ||
            !string.Equals(
                execution.DefinitionVersion,
                definition.Version,
                StringComparison.Ordinal))
        {
            return Task.FromResult(
                RecoveryDecision.RequireReview);
        }

        var hasPendingOperation =
            operations.Any(operation =>
                string.Equals(
                    operation.Status,
                    "Pending",
                    StringComparison.OrdinalIgnoreCase));

        var failedOperations =
            operations
                .Where(operation =>
                    string.Equals(
                        operation.Status,
                        "Failed",
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

        var hasUnknownOperation =
            operations.Any(operation =>
                !string.Equals(
                    operation.Status,
                    "Pending",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    operation.Status,
                    "Failed",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    operation.Status,
                    "Completed",
                    StringComparison.OrdinalIgnoreCase));

        if (hasUnknownOperation)
        {
            return Task.FromResult(
                RecoveryDecision.RequireReview);
        }

        var decision =
            execution.CurrentStatus switch
            {
                WorkflowStatus.Interrupted
                    when hasPendingOperation
                    => RecoveryDecision.RequireReview,

                WorkflowStatus.Interrupted
                    when failedOperations.Count == 1 &&
                         definition.ActivityIds.Contains(
                             failedOperations[0].ActivityId,
                             StringComparer.Ordinal) &&
                         definition.RetryableActivityIds.Contains(
                             failedOperations[0].ActivityId,
                             StringComparer.Ordinal)
                    => RecoveryDecision.Retry,

                WorkflowStatus.Interrupted
                    when failedOperations.Count > 0
                    => RecoveryDecision.RequireReview,

                WorkflowStatus.Interrupted
                    when checkpoints.Any(checkpoint =>
                        checkpoint.Status == WorkflowStatus.Completed)
                    => RecoveryDecision.Resume,

                WorkflowStatus.Failed
                    => RecoveryDecision.RequireReview,

                _
                    => RecoveryDecision.Failed
            };

        return Task.FromResult(decision);
    }
}
