using EMF.Core.Models.Workflow;
using EMF.Orchestration.Contracts;

namespace EMF.Orchestration.Services;

public sealed class WorkflowRecoveryPolicy : IWorkflowRecoveryPolicy
{
    public Task<RecoveryDecision> EvaluateAsync(
        WorkflowExecutionRecord execution,
        IReadOnlyList<WorkflowCheckpoint> checkpoints,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(checkpoints);

        var decision =
            execution.CurrentStatus switch
            {
                WorkflowStatus.Interrupted when checkpoints.Count > 0
                    => RecoveryDecision.Resume,

                WorkflowStatus.Failed
                    => RecoveryDecision.RequireReview,

                _
                    => RecoveryDecision.Failed
            };

        return Task.FromResult(decision);
    }
}
