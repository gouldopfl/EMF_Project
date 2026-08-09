using EMF.Core.Models.Workflow;
using EMF.Orchestration.Contracts;

namespace EMF.Orchestration.Services;

public sealed class WorkflowActivityResolver : IWorkflowActivityResolver
{
    private readonly IReadOnlyDictionary<string, IWorkflowActivity> _activities;

    public WorkflowActivityResolver(
        IEnumerable<IWorkflowActivity> activities)
    {
        ArgumentNullException.ThrowIfNull(activities);

        _activities = activities
            .ToDictionary(
                activity => activity.Id,
                StringComparer.Ordinal);
    }

    public IReadOnlyList<IWorkflowActivity> Resolve(
        WorkflowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var resolved = new List<IWorkflowActivity>();

        foreach (var activityId in definition.ActivityIds)
        {
            if (!_activities.TryGetValue(
                activityId,
                out var activity))
            {
                throw new InvalidOperationException(
                    $"Workflow activity '{activityId}' was not found.");
            }

            if (resolved.Any(x =>
                string.Equals(
                    x.Id,
                    activityId,
                    StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Workflow activity '{activityId}' is specified more than once.");
            }

            resolved.Add(activity);
        }

        return resolved;
    }
}
