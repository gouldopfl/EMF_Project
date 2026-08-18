using EMF.Core.Models.Workflow;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class WorkflowRunner : IWorkflowRunner
{
    private readonly IWorkflowService _workflowService;
    private readonly WorkflowActivityClaimHeartbeatOptions
        _heartbeatOptions;

    public WorkflowRunner(IWorkflowService workflowService)
        : this(
            workflowService,
            new WorkflowActivityClaimHeartbeatOptions())
    {
    }

    public WorkflowRunner(
        IWorkflowService workflowService,
        WorkflowActivityClaimHeartbeatOptions heartbeatOptions)
    {
        ArgumentNullException.ThrowIfNull(workflowService);
        ArgumentNullException.ThrowIfNull(heartbeatOptions);

        _workflowService = workflowService;
        _heartbeatOptions = heartbeatOptions;
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

            var claimId = Guid.NewGuid().ToString("N");

            var claimed =
                await _workflowService.TryClaimActivityAsync(
                    context.WorkflowId,
                    activity.Id,
                    claimId,
                    DateTimeOffset.UtcNow,
                    cancellationToken);

            if (!claimed)
            {
                return;
            }

            var result =
                await ExecuteWithClaimHeartbeatAsync(
                    context,
                    activity,
                    claimId,
                    cancellationToken);

            await _workflowService.RecordCheckpointAsync(
                new WorkflowCheckpoint
                {
                    WorkflowId = context.WorkflowId,
                    Step = activity.Name,
                    ActivityId = activity.Id,
                    Status = result.Succeeded
                        ? WorkflowStatus.Completed
                        : WorkflowStatus.Failed,
                    RecordedUtc = result.CompletedUtc,
                    Message = result.Message
                },
                cancellationToken);

            if (result.Succeeded)
            {
                await _workflowService.CompleteActivityClaimAsync(
                    context.WorkflowId,
                    activity.Id,
                    claimId,
                    result.CompletedUtc,
                    cancellationToken);

                continue;
            }

            await _workflowService.ReleaseActivityClaimAsync(
                context.WorkflowId,
                activity.Id,
                claimId,
                cancellationToken);

            await _workflowService.FailAsync(
                context.WorkflowId,
                result.Message ?? "Workflow activity failed.",
                cancellationToken);

            return;
        }

        await _workflowService.CompleteAsync(
            context.WorkflowId,
            cancellationToken);
    }

    private async Task<WorkflowActivityResult>
        ExecuteWithClaimHeartbeatAsync(
            WorkflowExecutionContext context,
            IWorkflowActivity activity,
            string claimId,
            CancellationToken cancellationToken)
    {
        using var executionCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        var activityTask =
            activity.ExecuteAsync(
                context,
                executionCancellation.Token);

        var heartbeatTask =
            RunHeartbeatAsync(
                context.WorkflowId,
                activity.Id,
                claimId,
                executionCancellation.Token);

        var completed =
            await Task.WhenAny(
                activityTask,
                heartbeatTask);

        if (completed == heartbeatTask)
        {
            executionCancellation.Cancel();

            try
            {
                await activityTask;
            }
            catch (OperationCanceledException)
                when (executionCancellation.IsCancellationRequested)
            {
            }

            await heartbeatTask;

            throw new InvalidOperationException(
                "Workflow activity claim heartbeat stopped unexpectedly.");
        }

        var result =
            await activityTask;

        executionCancellation.Cancel();

        try
        {
            await heartbeatTask;
        }
        catch (OperationCanceledException)
            when (executionCancellation.IsCancellationRequested)
        {
        }

        return result;
    }

    private async Task RunHeartbeatAsync(
        EMF.Core.Models.Identities.WorkflowId workflowId,
        string activityId,
        string claimId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(
                _heartbeatOptions.Interval,
                cancellationToken);

            var renewed =
                await _workflowService.TryRenewActivityClaimAsync(
                    workflowId,
                    activityId,
                    claimId,
                    DateTimeOffset.UtcNow,
                    cancellationToken);

            if (!renewed)
            {
                throw new InvalidOperationException(
                    "Workflow activity claim ownership was lost.");
            }
        }
    }
}
