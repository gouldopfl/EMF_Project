using EMF.Core.Models.Identities;
using EMF.Persistence.Repositories;
using EMF.Security;
using EMF.Security.Models;
using EMF.Security.Models.Identities;
using EMF.Security.Persistence.Sqlite.Auditing;

namespace EMF.ConsoleApplication;

internal static class SecurityWorkflowRecoveryConsoleCommand
{
    public static async Task<int> RunAsync(
        string[] args)
    {
        if (args.Length != 4)
            return 2;

        var databasePath =
            Path.GetFullPath(args[0]);

        var recoveryOptions =
            new WorkflowActivityClaimRecoveryOptions();

        var reclaimedUtc =
            DateTimeOffset.UtcNow;

        var abandonedBeforeUtc =
            recoveryOptions
                .CalculateAbandonedBeforeUtc(
                    reclaimedUtc);

        var subjectId =
            Environment.GetEnvironmentVariable(
                "EMF_SUBJECT_ID") ??
            "console-steward";

        var classification =
            new ProtectionClassificationId(
                Environment.GetEnvironmentVariable(
                    "EMF_PROTECTION_CLASSIFICATION") ??
                "confidential");

        var auditPath =
            Environment.GetEnvironmentVariable(
                "EMF_SECURITY_AUDIT_DATABASE") ??
            Path.Combine(
                AppContext.BaseDirectory,
                "emf-security-audit.db");

        var repository =
            new SqliteWorkflowRepository(
                databasePath);

        await repository.InitializeAsync();

        var auditSink =
            new SqliteSecurityAuditSink(
                auditPath);

        await auditSink.InitializeAsync();

        var authorization =
            ConsoleAuthorizationPolicyFactory.Create(
                subjectId,
                SecurityPermissions.WorkflowActivityClaimRecover);

        var service =
            new WorkflowActivityClaimRecoveryService(
                repository,
                authorization,
                auditSink);

        var recovered =
            await service.RecoverAsync(
                new WorkflowActivityClaimRecoveryRequest
                {
                    SubjectId = subjectId,
                    WorkflowId =
                        new WorkflowId(args[1]),
                    ActivityId = args[2],
                    NewClaimId = args[3],
                    ReclaimedUtc =
                        reclaimedUtc,
                    AbandonedBeforeUtc =
                        abandonedBeforeUtc,
                    ProtectionClassificationId =
                        classification
                });

        return recovered ? 0 : 1;
    }
}
