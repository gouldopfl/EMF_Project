using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class WorkflowExecutionCoordinator
{
    private readonly IWorkflowService _workflowService;
    private readonly IWorkflowRecoveryCoordinator _recoveryCoordinator;
    private readonly IWorkflowRunner _runner;

    public WorkflowExecutionCoordinator(
        IWorkflowService workflowService,
        IWorkflowRecoveryCoordinator recoveryCoordinator,
        IWorkflowRunner runner)
    {
        ArgumentNullException.ThrowIfNull(workflowService);
        ArgumentNullException.ThrowIfNull(recoveryCoordinator);
        ArgumentNullException.ThrowIfNull(runner);

        _workflowService = workflowService;
        _recoveryCoordinator = recoveryCoordinator;
        _runner = runner;
    }

    public async Task ExecuteAsync(
        WorkflowDefinition definition,
        IEnumerable<IWorkflowActivity> activities,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(activities);

        var workflowId =
            await _workflowService.StartAsync(
                definition,
                cancellationToken);

        var context =
            new WorkflowExecutionContext
            {
                WorkflowId = workflowId
            };

        await _runner.ExecuteAsync(
            context,
            activities,
            cancellationToken);
    }

    public async Task ExecuteRecoveryAsync(
        WorkflowId workflowId,
        WorkflowDefinition definition,
        IEnumerable<IWorkflowActivity> activities,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(activities);

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

        var context =
            new WorkflowExecutionContext
            {
                WorkflowId = workflowId
            };

        await _runner.ExecuteAsync(
            context,
            activities,
            cancellationToken);
    }
}
