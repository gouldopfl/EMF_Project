using EMF.Core.Models.Identities;

namespace EMF.Core.Models.Workflow;

public sealed class WorkflowOperationRecord
{
    public required WorkflowId WorkflowId { get; init; }

    public required string ActivityId { get; init; }

    public required OperationId OperationId { get; init; }

    public required string OperationType { get; init; }

    public required string Status { get; init; }

    public required DateTimeOffset CreatedUtc { get; init; }

    public DateTimeOffset? CompletedUtc { get; init; }
}
