using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class WorkflowExecutionCoordinator
{
    private readonly IWorkflowService _workflowService;
    private readonly IWorkflowRecoveryCoordinator _recoveryCoordinator;
    private readonly IWorkflowActivityResolver _activityResolver;
    private readonly IWorkflowRunner _runner;

    public WorkflowExecutionCoordinator(
        IWorkflowService workflowService,
        IWorkflowRecoveryCoordinator recoveryCoordinator,
        IWorkflowActivityResolver activityResolver,
        IWorkflowRunner runner)
    {
        ArgumentNullException.ThrowIfNull(workflowService);
        ArgumentNullException.ThrowIfNull(recoveryCoordinator);
        ArgumentNullException.ThrowIfNull(activityResolver);
        ArgumentNullException.ThrowIfNull(runner);

        _workflowService = workflowService;
        _recoveryCoordinator = recoveryCoordinator;
        _activityResolver = activityResolver;
        _runner = runner;
    }

    public async Task ExecuteAsync(
        WorkflowDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var workflowId =
            await _workflowService.StartAsync(
                definition,
                cancellationToken);

        var context =
            new WorkflowExecutionContext
            {
                WorkflowId = workflowId
            };

        var activities = _activityResolver.Resolve(definition);

        await _runner.ExecuteAsync(
            context,
            activities,
            cancellationToken);
    }

    public async Task ExecuteRecoveryAsync(
        WorkflowId workflowId,
        WorkflowDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

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

        var activities = _activityResolver.Resolve(definition);

        await _runner.ExecuteAsync(
            context,
            activities,
            cancellationToken);
    }
}
