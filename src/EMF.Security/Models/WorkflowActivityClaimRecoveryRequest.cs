using EMF.Core.Models.Identities;
using EMF.Security.Models.Identities;

namespace EMF.Security.Models;

public sealed class WorkflowActivityClaimRecoveryRequest
{
    public required string SubjectId { get; init; }
    public required WorkflowId WorkflowId { get; init; }
    public required string ActivityId { get; init; }
    public required string NewClaimId { get; init; }
    public required DateTimeOffset ReclaimedUtc { get; init; }
    public required DateTimeOffset AbandonedBeforeUtc { get; init; }
    public required ProtectionClassificationId
        ProtectionClassificationId { get; init; }
}
