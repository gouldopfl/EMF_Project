using EMF.Core.Models.Identities;

namespace EMF.Core.Models.Workflow;

public sealed class WorkflowStatusTransition
{
    public required WorkflowId WorkflowId { get; init; }

    public required WorkflowStatus FromStatus { get; init; }

    public required WorkflowStatus ToStatus { get; init; }

    public required DateTimeOffset RecordedUtc { get; init; }

    public string? Message { get; init; }
}
