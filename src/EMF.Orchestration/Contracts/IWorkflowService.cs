using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;

namespace EMF.Orchestration.Contracts;

public interface IWorkflowService
{
    Task<WorkflowId> StartAsync(
        WorkflowDefinition definition,
        CancellationToken cancellationToken = default);

    Task RecordCheckpointAsync(
        WorkflowCheckpoint checkpoint,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowCheckpoint>> GetCheckpointsAsync(
        WorkflowId workflowId,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(
        WorkflowId workflowId,
        CancellationToken cancellationToken = default);

    Task FailAsync(
        WorkflowId workflowId,
        string message,
        CancellationToken cancellationToken = default);

    Task<bool> TryClaimActivityAsync(
        WorkflowId workflowId,
        string activityId,
        string claimId,
        DateTimeOffset claimedUtc,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "Workflow activity claims are not supported by this service.");
    }

    Task<bool> TryRenewActivityClaimAsync(
        WorkflowId workflowId,
        string activityId,
        string claimId,
        DateTimeOffset renewedUtc,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "Workflow activity claim renewal is not supported by this service.");
    }

    Task CompleteActivityClaimAsync(
        WorkflowId workflowId,
        string activityId,
        string claimId,
        DateTimeOffset completedUtc,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "Workflow activity claims are not supported by this service.");
    }

    Task ReleaseActivityClaimAsync(
        WorkflowId workflowId,
        string activityId,
        string claimId,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "Workflow activity claims are not supported by this service.");
    }
}
