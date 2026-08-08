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

        var activityList = activities.ToList();

        if (activityList.Any(x => string.IsNullOrWhiteSpace(x.Id)))
        {
            throw new InvalidOperationException(
                "Workflow activity IDs must not be blank.");
        }

        if (activityList
            .GroupBy(x => x.Id, StringComparer.Ordinal)
            .Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException(
                "Workflow activity IDs must be unique.");
        }

        var checkpoints = await _workflowService.GetCheckpointsAsync(
            context.WorkflowId,
            cancellationToken);

        var completedActivities = checkpoints
            .Where(x => x.Status == WorkflowStatus.Completed)
            .Where(x => x.ActivityId is not null)
            .Select(x => x.ActivityId!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var activity in activityList)
        {
            if (completedActivities.Contains(activity.Id))
            {
                continue;
            }

            var result = await activity.ExecuteAsync(
                context,
                cancellationToken);

            await _workflowService.RecordCheckpointAsync(
                new WorkflowCheckpoint
                {
                    WorkflowId = context.WorkflowId,
                    Step = result.ActivityName,
                    ActivityId = activity.Id,
                    Status = result.Succeeded
                        ? WorkflowStatus.Completed
                        : WorkflowStatus.Failed,
                    RecordedUtc = result.CompletedUtc,
                    Message = result.Message
                },
                cancellationToken);

            if (!result.Succeeded)
            {
                break;
            }
        }
    }
}
