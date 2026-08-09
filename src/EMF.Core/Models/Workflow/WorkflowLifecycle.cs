namespace EMF.Core.Models.Workflow;

public static class WorkflowLifecycle
{
    public static bool CanTransition(
        WorkflowStatus current,
        WorkflowStatus next)
    {
        return (current, next) switch
        {
            (WorkflowStatus.Pending, WorkflowStatus.Running) => true,

            (WorkflowStatus.Running, WorkflowStatus.Completed) => true,
            (WorkflowStatus.Running, WorkflowStatus.Failed) => true,
            (WorkflowStatus.Running, WorkflowStatus.Interrupted) => true,
            (WorkflowStatus.Running, WorkflowStatus.Paused) => true,

            (WorkflowStatus.Paused, WorkflowStatus.Running) => true,
            (WorkflowStatus.Paused, WorkflowStatus.Interrupted) => true,

            (WorkflowStatus.Interrupted, WorkflowStatus.Running) => true,
            (WorkflowStatus.Interrupted, WorkflowStatus.Failed) => true,

            _ => false
        };
    }
}
