using EMF.Orchestration.Models;

namespace EMF.Orchestration.Contracts;

public interface IWorkflowActivity
{
    string Id { get; }

    string Name { get; }

    Task<WorkflowActivityResult> ExecuteAsync(
        WorkflowExecutionContext context,
        CancellationToken cancellationToken = default);
}
