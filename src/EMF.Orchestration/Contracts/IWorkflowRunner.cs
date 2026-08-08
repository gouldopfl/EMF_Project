using EMF.Orchestration.Models;

namespace EMF.Orchestration.Contracts;

public interface IWorkflowRunner
{
    Task ExecuteAsync(
        WorkflowExecutionContext context,
        IEnumerable<IWorkflowActivity> activities,
        CancellationToken cancellationToken = default);
}
