using EMF.Core.Contracts;
using EMF.Core.Models.Workflow;
using EMF.Orchestration.Contracts;

namespace EMF.Orchestration.Services;

public sealed class WorkflowDefinitionService :
    IWorkflowDefinitionService
{
    private readonly IWorkflowDefinitionRepository _repository;

    public WorkflowDefinitionService(
        IWorkflowDefinitionRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        _repository = repository;
    }

    public Task RegisterAsync(
        WorkflowDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return _repository.StoreDefinitionAsync(
            definition,
            cancellationToken);
    }

    public Task<WorkflowDefinition?> ResolveAsync(
        string definitionId,
        string version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            definitionId);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            version);

        return _repository.GetDefinitionAsync(
            definitionId,
            version,
            cancellationToken);
    }
}
