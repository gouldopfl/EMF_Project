using EMF.Core.Models.Identities;

namespace EMF.Core.Models.Workflow;

public sealed class WorkflowExecutionRecord
{
    public required WorkflowId WorkflowId { get; init; }

    public required string DefinitionId { get; init; }

    public required string DefinitionVersion { get; init; }

    public required DateTimeOffset CreatedUtc { get; init; }

    public required WorkflowStatus CurrentStatus { get; init; }
}
