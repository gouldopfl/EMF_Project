using EMF.Core.Models.Workflow;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class WorkflowRunner : IWorkflowRunner
{
    private readonly IWorkflowService _workflowService;

    public WorkflowRunner(IWorkflowService workflowService)
    {
        ArgumentNullException.ThrowIfNull(workflowService);
        _workflowService = workflowService;
    }

    public async Task ExecuteAsync(
        WorkflowExecutionContext context,
        IEnumerable<IWorkflowActivity> activities,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(activities);

        foreach (var activity in activities)
        {
            var result = await activity.ExecuteAsync(
                context,
                cancellationToken);

            await _workflowService.RecordCheckpointAsync(
                new WorkflowCheckpoint
                {
                    WorkflowId = context.WorkflowId,
                    Step = result.ActivityName,
                    Status = result.Succeeded
                        ? WorkflowStatus.Completed
                        : WorkflowStatus.Failed,
                    RecordedUtc = result.CompletedUtc,
                    Message = result.Message
                },
                cancellationToken);
        }
    }
}
