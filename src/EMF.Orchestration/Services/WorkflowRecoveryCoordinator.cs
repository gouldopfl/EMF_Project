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

        string? retryActivityId = null;
        OperationId? retryOperationId = null;

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
                else
                {
                    retryActivityId = failedOperation.ActivityId;
                    retryOperationId = failedOperation.OperationId;
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

        var updatedExecution =
            new WorkflowExecutionRecord
            {
                WorkflowId = execution.WorkflowId,
                DefinitionId = execution.DefinitionId,
                DefinitionVersion = execution.DefinitionVersion,
                CreatedUtc = execution.CreatedUtc,
                CurrentStatus = execution.CurrentStatus,
                RecoveryStatus = recoveryStatus,
                Revision = execution.Revision
            };

        var decisionRecord =
            new WorkflowRecoveryDecisionRecord
            {
                WorkflowId = execution.WorkflowId,
                DefinitionId = execution.DefinitionId,
                DefinitionVersion = execution.DefinitionVersion,
                EvaluatedRevision = execution.Revision,
                Decision = decision,
                RetryActivityId = retryActivityId,
                RetryOperationId = retryOperationId,
                RecordedUtc = DateTimeOffset.UtcNow
            };

        await _repository.ApplyRecoveryDecisionAsync(
            updatedExecution,
            decisionRecord,
            cancellationToken);

        return new WorkflowRecoveryResult
        {
            Decision = decision,
            RetryActivityId = retryActivityId,
            RetryOperationId = retryOperationId
        };
    }
}
