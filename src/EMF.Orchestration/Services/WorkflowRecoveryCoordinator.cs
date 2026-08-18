using EMF.Core.Contracts;
using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class WorkflowRecoveryCoordinator : IWorkflowRecoveryCoordinator
{
    private readonly IWorkflowRepository _repository;
    private readonly IWorkflowRecoveryPolicy _policy;

    public WorkflowRecoveryCoordinator(
        IWorkflowRepository repository,
        IWorkflowRecoveryPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(policy);

        _repository = repository;
        _policy = policy;
    }

    public async Task<WorkflowRecoveryResult> RecoverAsync(
        WorkflowId workflowId,
        WorkflowDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var execution =
            await _repository.GetExecutionAsync(
                workflowId,
                cancellationToken);

        if (execution is null)
        {
            return new WorkflowRecoveryResult
            {
                Decision = RecoveryDecision.Failed
            };
        }

        if (!string.Equals(
                execution.DefinitionId,
                definition.Id,
                StringComparison.Ordinal)
            || !string.Equals(
                execution.DefinitionVersion,
                definition.Version,
                StringComparison.Ordinal))
        {
            return new WorkflowRecoveryResult
            {
                Decision = RecoveryDecision.Failed
            };
        }

        var checkpoints =
            await _repository.GetCheckpointsAsync(
                workflowId,
                cancellationToken);

        var operations = await _repository.GetOperationsAsync(
            workflowId, cancellationToken);

        var decision =
            await _policy.EvaluateAsync(
                execution,
                definition,
                checkpoints,
                operations,
                cancellationToken);

        if (decision == RecoveryDecision.Retry)
        {
            var failedOperations =
                operations
                    .Where(operation =>
                        string.Equals(
                            operation.Status,
                            "Failed",
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (failedOperations.Count != 1)
            {
                decision = RecoveryDecision.RequireReview;
            }
            else
            {
                var failedOperation = failedOperations[0];

                var activityExists =
                    definition.ActivityIds.Any(activityId =>
                        string.Equals(
                            activityId,
                            failedOperation.ActivityId,
                            StringComparison.Ordinal));

                if (!activityExists)
                {
                    decision = RecoveryDecision.RequireReview;
                }
            }
        }

        var recoveryStatus =
            decision switch
            {
                RecoveryDecision.Resume or RecoveryDecision.Retry
                    => WorkflowRecoveryStatus.Recoverable,

                RecoveryDecision.RequireReview
                    => WorkflowRecoveryStatus.NeedsReview,

                _ => execution.RecoveryStatus
            };

        if (recoveryStatus != execution.RecoveryStatus)
        {
            await _repository.UpdateExecutionAsync(
                new WorkflowExecutionRecord
                {
                    WorkflowId = execution.WorkflowId,
                    DefinitionId = execution.DefinitionId,
                    DefinitionVersion = execution.DefinitionVersion,
                    CreatedUtc = execution.CreatedUtc,
                    CurrentStatus = execution.CurrentStatus,
                    RecoveryStatus = recoveryStatus,
                    Revision = execution.Revision
                },
                cancellationToken);
        }

        if (decision == RecoveryDecision.Retry)
        {
            var failedOperation =
                operations.Single(operation =>
                    string.Equals(
                        operation.Status,
                        "Failed",
                        StringComparison.OrdinalIgnoreCase));

            return new WorkflowRecoveryResult
            {
                Decision = decision,
                RetryActivityId = failedOperation.ActivityId,
                RetryOperationId = failedOperation.OperationId
            };
        }

        return new WorkflowRecoveryResult
        {
            Decision = decision
        };
    }
}
