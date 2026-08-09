using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class WorkflowExecutionCoordinator
{
    private readonly IWorkflowRecoveryCoordinator _recoveryCoordinator;
    private readonly IWorkflowRunner _runner;

    public WorkflowExecutionCoordinator(
        IWorkflowRecoveryCoordinator recoveryCoordinator,
        IWorkflowRunner runner)
    {
        ArgumentNullException.ThrowIfNull(recoveryCoordinator);
        ArgumentNullException.ThrowIfNull(runner);

        _recoveryCoordinator = recoveryCoordinator;
        _runner = runner;
    }

    public async Task ExecuteRecoveryAsync(
        WorkflowId workflowId,
        WorkflowDefinition definition,
        WorkflowExecutionContext context,
        IEnumerable<IWorkflowActivity> activities,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(activities);

        if (context.WorkflowId != workflowId)
        {
            throw new InvalidOperationException(
                "Workflow execution context does not match the requested workflow.");
        }

        var decision =
            await _recoveryCoordinator.RecoverAsync(
                workflowId,
                definition,
                cancellationToken);

        if (decision is not RecoveryDecision.Resume
            and not RecoveryDecision.Retry)
        {
            return;
        }

        await _runner.ExecuteAsync(
            context,
            activities,
            cancellationToken);
    }
}
