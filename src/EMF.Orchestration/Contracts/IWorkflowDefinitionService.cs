using EMF.Core.Models.Workflow;

namespace EMF.Orchestration.Contracts;

public interface IWorkflowDefinitionService
{
    Task RegisterAsync(
        WorkflowDefinition definition,
        CancellationToken cancellationToken = default);

    Task<WorkflowDefinition?> ResolveAsync(
        string definitionId,
        string version,
        CancellationToken cancellationToken = default);
}
