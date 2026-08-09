using EMF.Core.Models.Workflow;

namespace EMF.Orchestration.Contracts;

public interface IWorkflowRecoveryPolicy
{
    Task<RecoveryDecision> EvaluateAsync(
        WorkflowExecutionRecord execution,
        IReadOnlyList<WorkflowCheckpoint> checkpoints,
        CancellationToken cancellationToken = default);
}
