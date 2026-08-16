namespace EMF.Security.Persistence.Sqlite.Auditing;

public sealed record
    SecurityAuditIntegrityVerificationResult
{
    public required bool IsValid { get; init; }

    public required int ProtectedRecordCount
    { get; init; }

    public required int LegacyRecordCount
    { get; init; }

    public long? InvalidRecordId { get; init; }

    public string? FailureReason { get; init; }
}
