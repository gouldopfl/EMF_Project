using EMF.Security.Auditing;
using EMF.Security.Auditing.Models;

namespace EMF.Tests;

public sealed partial class
    IntelligenceCapabilityExecutorTests
{
    private sealed class RecordingAuditSink :
        ISecurityAuditSink
    {
        public Exception? Failure { get; init; }

        public List<SecurityAuditRecord> Records
        {
            get;
        } = [];

        public Task WriteAsync(
            SecurityAuditRecord record,
            CancellationToken cancellationToken =
                default)
        {
            if (Failure is not null)
            {
                return Task.FromException(Failure);
            }

            Records.Add(record);

            return Task.CompletedTask;
        }
    }
}
