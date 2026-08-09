using EMF.Core.Models.Workflow;

namespace EMF.Orchestration.Contracts;

public interface IWorkflowActivityResolver
{
    IReadOnlyList<IWorkflowActivity> Resolve(
        WorkflowDefinition definition);
}
