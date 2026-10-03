using EMF.Security.Auditing;
using EMF.Security.Auditing.Models;

namespace EMF.Tests;

internal sealed class RecordingSecurityAuditSink :
    IAcknowledgedSecurityAuditSink
{
    private readonly List<SecurityAuditRecord>
        _records = [];

    public IReadOnlyList<SecurityAuditRecord> Records =>
        _records;

    public Task WriteAsync(
        SecurityAuditRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        _records.Add(record);

        return Task.CompletedTask;
    }
    public Task<SecurityAuditAcknowledgement> AppendAsync(SecurityAuditRecord record, CancellationToken cancellationToken = default)
    {
        var bytes = SecurityAuditCanonicalEvent.Encode(record);
        var existing = _records.FirstOrDefault(r => r.AuditEventId == record.AuditEventId);
        if (existing is not null && !bytes.AsSpan().SequenceEqual(SecurityAuditCanonicalEvent.Encode(existing))) throw new SecurityAuditIdentityConflictException();
        if (existing is null) _records.Add(record);
        return Task.FromResult(new SecurityAuditAcknowledgement(record.AuditEventId!.Value, _records.IndexOf(existing ?? record) + 1, new string('A', 64)));
    }
    public Task<VerifiedSecurityAuditEvent?> FindVerifiedAsync(SecurityAuditEventId id, CancellationToken cancellationToken = default)
    {
        var record = _records.FirstOrDefault(r => r.AuditEventId == id);
        return Task.FromResult(record is null ? null : new VerifiedSecurityAuditEvent(record, new(id, _records.IndexOf(record) + 1, new string('A', 64))));
    }
}
