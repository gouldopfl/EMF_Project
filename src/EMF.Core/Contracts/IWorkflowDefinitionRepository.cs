using EMF.Core.Models.Workflow;

namespace EMF.Core.Contracts;

public interface IWorkflowDefinitionRepository
{
    Task StoreDefinitionAsync(
        WorkflowDefinition definition,
        CancellationToken cancellationToken = default);

    Task<WorkflowDefinition?> GetDefinitionAsync(
        string definitionId,
        string version,
        CancellationToken cancellationToken = default);
}
