using EMF.Security.Monitoring;

namespace EMF.Security.Persistence.Sqlite.Auditing;

public sealed record
    SecurityAuditIntegrityVerificationResult
{
    public required bool IsValid { get; init; }

    /// <summary>Protected records processed before failure, or total protected records on success.</summary>
    public required int ProtectedRecordCount
    { get; init; }

    /// <summary>Legal legacy prefix records processed before failure, or total legacy records on success.</summary>
    public required int LegacyRecordCount
    { get; init; }

    public long? LastProtectedRecordId
    { get; init; }

    public string? ChainHeadHash { get; init; }

    public long? InvalidRecordId { get; init; }

    public string? FailureReason { get; init; }

    public SecurityAuditIntegrityFailureCategory? FailureCategory { get; init; }

    // On invalid results the counts describe records processed before failure.
    // Invalid results deliberately expose no chain head or hash.
}
