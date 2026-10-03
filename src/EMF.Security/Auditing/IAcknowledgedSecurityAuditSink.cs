using EMF.Security.Auditing.Models;
namespace EMF.Security.Auditing;

public sealed record SecurityAuditAcknowledgement(SecurityAuditEventId EventId, long RecordId, string RecordHash);
public sealed record VerifiedSecurityAuditEvent(SecurityAuditRecord Record, SecurityAuditAcknowledgement Acknowledgement);
public interface IAcknowledgedSecurityAuditSink : ISecurityAuditSink
{
    Task<SecurityAuditAcknowledgement> AppendAsync(SecurityAuditRecord record, CancellationToken cancellationToken = default);
    // Missing returns null; unavailable or damaged chain throws, never masquerades as acknowledgement.
    Task<VerifiedSecurityAuditEvent?> FindVerifiedAsync(SecurityAuditEventId eventId, CancellationToken cancellationToken = default);
}
public sealed class SecurityAuditIdentityConflictException : InvalidOperationException
{
    public SecurityAuditIdentityConflictException() : base("Canonical audit event identity conflicts with existing data.") { }
}
