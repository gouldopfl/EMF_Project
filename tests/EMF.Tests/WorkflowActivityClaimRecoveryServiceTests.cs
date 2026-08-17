using EMF.Core.Models.Identities;
using EMF.Security;
using EMF.Security.Auditing.Models;
using EMF.Security.Authorization;
using EMF.Security.Authorization.Services;
using EMF.Security.Models;
using EMF.Security.Models.Identities;
using EMF.Persistence.Repositories;

namespace EMF.Tests;

public sealed class WorkflowActivityClaimRecoveryServiceTests
{
    [Fact]
    public async Task Authorized_recovery_is_audited()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-secure-recovery-{Guid.NewGuid():N}.db");

        try
        {
            var repository =
                new SqliteWorkflowRepository(path);

            await repository.InitializeAsync();

            var workflowId = new WorkflowId("workflow-001");
            var now = DateTimeOffset.UtcNow;

            await repository.TryClaimActivityAsync(
                workflowId, "activity", "old-claim",
                now.AddHours(-1));

            var context = new AuthorizationContext
            {
                SubjectId = "steward",
                RoleIds = [],
                PermissionIds =
                [
                    SecurityPermissions
                        .WorkflowActivityClaimRecover
                ]
            };

            var audit = new RecordingSecurityAuditSink();

            var service =
                new WorkflowActivityClaimRecoveryService(
                    repository,
                    new AuthorizationPolicy(
                        new InMemoryAuthorizationContextProvider(
                            [context])),
                    audit);

            var recovered = await service.RecoverAsync(
                new WorkflowActivityClaimRecoveryRequest
                {
                    SubjectId = "steward",
                    WorkflowId = workflowId,
                    ActivityId = "activity",
                    NewClaimId = "new-claim",
                    ReclaimedUtc = now,
                    AbandonedBeforeUtc =
                        now.AddMinutes(-5),
                    ProtectionClassificationId =
                        new ProtectionClassificationId(
                            "internal")
                });

            Assert.True(recovered);

            var record = Assert.Single(audit.Records);

            Assert.Equal(
                SecurityAuditOutcome.Succeeded,
                record.Outcome);
            Assert.Equal("Workflow", record.ResourceType);
            Assert.Equal(workflowId.Value, record.ResourceId);
            Assert.Equal("steward", record.SubjectId);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task Denied_recovery_is_audited()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-denied-recovery-{Guid.NewGuid():N}.db");

        try
        {
            var repository =
                new SqliteWorkflowRepository(path);

            await repository.InitializeAsync();

            var workflowId = new WorkflowId("workflow-denied");
            var now = DateTimeOffset.UtcNow;

            await repository.TryClaimActivityAsync(
                workflowId, "activity", "old-claim",
                now.AddHours(-1));

            var context = new AuthorizationContext
            {
                SubjectId = "viewer",
                RoleIds = [],
                PermissionIds = []
            };

            var audit = new RecordingSecurityAuditSink();

            var service =
                new WorkflowActivityClaimRecoveryService(
                    repository,
                    new AuthorizationPolicy(
                        new InMemoryAuthorizationContextProvider(
                            [context])),
                    audit);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => service.RecoverAsync(
                    new WorkflowActivityClaimRecoveryRequest
                    {
                        SubjectId = "viewer",
                        WorkflowId = workflowId,
                        ActivityId = "activity",
                        NewClaimId = "new-claim",
                        ReclaimedUtc = now,
                        AbandonedBeforeUtc =
                            now.AddMinutes(-5),
                        ProtectionClassificationId =
                            new("internal")
                    }));

            var record = Assert.Single(audit.Records);

            Assert.Equal(
                SecurityAuditOutcome.Denied,
                record.Outcome);
            Assert.Equal(
                AuthorizationDecision.Deny,
                record.PolicyDecision);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
