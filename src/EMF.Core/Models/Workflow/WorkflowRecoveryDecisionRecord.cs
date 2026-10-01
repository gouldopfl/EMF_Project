using EMF.Core.Models.Identities;

namespace EMF.Core.Models.Workflow;

public sealed class WorkflowRecoveryDecisionRecord
{
    public required WorkflowId WorkflowId { get; init; }

    public required string DefinitionId { get; init; }

    public required string DefinitionVersion { get; init; }

    public required long EvaluatedRevision { get; init; }

    public required RecoveryDecision Decision { get; init; }

    public string? RetryActivityId { get; init; }

    public OperationId? RetryOperationId { get; init; }

    public required DateTimeOffset RecordedUtc { get; init; }
}
