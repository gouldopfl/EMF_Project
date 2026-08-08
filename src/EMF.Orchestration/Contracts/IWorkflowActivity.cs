using EMF.Orchestration.Models;

namespace EMF.Orchestration.Contracts;

public interface IWorkflowActivity
{
    string Name { get; }

    Task ExecuteAsync(
        WorkflowExecutionContext context,
        CancellationToken cancellationToken = default);
}
