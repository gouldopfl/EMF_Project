using EMF.Core.Models.Workflow;

namespace EMF.Orchestration.Contracts;

public interface IWorkflowRecoveryPolicy
{
    Task<RecoveryDecision> EvaluateAsync(
        WorkflowExecutionRecord execution,
        WorkflowDefinition definition,
        IReadOnlyList<WorkflowCheckpoint> checkpoints,
        IReadOnlyList<WorkflowOperationRecord> operations,
        CancellationToken cancellationToken = default);
}
