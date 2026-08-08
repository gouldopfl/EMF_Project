using EMF.Core.Models.Identities;

namespace EMF.Core.Models.Workflow;

public sealed class WorkflowCheckpoint
{
    public required WorkflowId WorkflowId { get; init; }

    public required string Step { get; init; }

    public required WorkflowStatus Status { get; init; }

    public required DateTimeOffset RecordedUtc { get; init; }

    public string? Message { get; init; }
}
