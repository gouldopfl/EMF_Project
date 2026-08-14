using EMF.Security.Auditing;
using EMF.Security.Auditing.Models;

namespace EMF.Tests;

internal sealed class RecordingSecurityAuditSink :
    ISecurityAuditSink
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
}
