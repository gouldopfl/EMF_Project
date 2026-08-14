namespace EMF.Security.Auditing.Models;

public enum SecurityAuditOutcome
{
    Succeeded = 0,
    Skipped = 1,
    Denied = 2,
    Failed = 3,
    Cancelled = 4
}
