using EMF.Security.Auditing.Models;

namespace EMF.Security.Auditing;

public interface ISecurityAuditSink
{
    Task WriteAsync(
        SecurityAuditRecord record,
        CancellationToken cancellationToken = default);
}
