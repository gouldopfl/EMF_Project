using EMF.Security.Authorization;

namespace EMF.Security.Auditing.Models;

public sealed class SecurityAuditRecord
{
    public SecurityAuditEventId? AuditEventId { get; init; }
    public SecurityMutationOperationId? OperationId { get; init; }
    public string? OriginalActorId { get; init; }
    public string? ServiceActorId { get; init; }
    public string? RecoveryActorId { get; init; }

    public required string Operation { get; init; }

    public required string ResourceType { get; init; }

    public required string ResourceId { get; init; }

    public required string SubjectId { get; init; }

    public AuthorizationDecision? PolicyDecision
    { get; init; }

    public string? Destination { get; init; }

    public required SecurityAuditOutcome Outcome
    { get; init; }

    public required DateTimeOffset OccurredUtc
    { get; init; }

    public IReadOnlyDictionary<string, string> Facts
    { get; init; } =
        new Dictionary<string, string>(
            StringComparer.Ordinal);
}
