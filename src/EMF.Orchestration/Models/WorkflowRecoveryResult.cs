using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;

namespace EMF.Orchestration.Models;

public sealed class WorkflowRecoveryResult
{
    public required RecoveryDecision Decision { get; init; }

    public string? RetryActivityId { get; init; }

    public OperationId? RetryOperationId { get; init; }
}
