using EMF.Security.Auditing.Models;

namespace EMF.Security.Persistence.Sqlite.Auditing;

public sealed class SecurityAuditOperationReport
{
    public required string Operation { get; init; }

    public required int TotalCount { get; init; }

    public required IReadOnlyDictionary<
        SecurityAuditOutcome,
        int> OutcomeCounts
    { get; init; }

    public DateTimeOffset? FirstOccurredUtc { get; init; }

    public DateTimeOffset? LastOccurredUtc { get; init; }

    public string? ChainHeadHash { get; init; }
}
