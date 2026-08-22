using EMF.Core.Models.Identities;

namespace EMF.Orchestration.Models;

public sealed class WorkflowExecutionContext
{
    public required WorkflowId WorkflowId { get; init; }

    public OperationId? OperationId { get; init; }
}
