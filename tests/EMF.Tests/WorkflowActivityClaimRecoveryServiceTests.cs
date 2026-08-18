using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;
using EMF.Security;
using EMF.Security.Auditing.Models;
using EMF.Security.Auditing;
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

    [Fact]
    public async Task Fresh_claim_is_audited_as_skipped()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-skipped-recovery-{Guid.NewGuid():N}.db");

        try
        {
            var repository =
                new SqliteWorkflowRepository(path);

            await repository.InitializeAsync();

            var workflowId = new WorkflowId("workflow-fresh");
            var now = DateTimeOffset.UtcNow;

            await repository.TryClaimActivityAsync(
                workflowId, "activity", "current-claim", now);

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
                        new("internal")
                });

            Assert.False(recovered);

            Assert.Equal(
                SecurityAuditOutcome.Skipped,
                Assert.Single(audit.Records).Outcome);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task Successful_recovery_propagates_audit_failure()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-audit-failure-{Guid.NewGuid():N}.db");

        try
        {
            var repository =
                new SqliteWorkflowRepository(path);

            await repository.InitializeAsync();

            var workflowId =
                new WorkflowId("workflow-audit-failure");

            var now =
                DateTimeOffset.UtcNow;

            Assert.True(
                await repository.TryClaimActivityAsync(
                    workflowId,
                    "activity",
                    "old-claim",
                    now.AddHours(-1)));

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

            var auditFailure =
                new InvalidOperationException(
                    "Synthetic audit failure.");

            var service =
                new WorkflowActivityClaimRecoveryService(
                    repository,
                    new AuthorizationPolicy(
                        new InMemoryAuthorizationContextProvider(
                            [context])),
                    new ThrowingSecurityAuditSink(auditFailure));

            var request =
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
                        new("internal")
                };

            var exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => service.RecoverAsync(request));

            Assert.Same(
                auditFailure,
                exception);

            await Assert.ThrowsAsync<WorkflowActivityClaimException>(
                () => repository.CompleteActivityClaimAsync(
                    workflowId,
                    "activity",
                    "old-claim",
                    now));

            await repository.CompleteActivityClaimAsync(
                workflowId,
                "activity",
                "new-claim",
                now);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task Repository_failure_is_audited()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString(),
            "missing",
            "workflow.db");

        var audit = new RecordingSecurityAuditSink();

        var service =
            new WorkflowActivityClaimRecoveryService(
                new SqliteWorkflowRepository(path),
                new AlwaysAllowAuthorizationPolicy(),
                audit);

        var request =
            new WorkflowActivityClaimRecoveryRequest
            {
                SubjectId = "steward",
                WorkflowId = new("workflow-failure"),
                ActivityId = "activity",
                NewClaimId = "new-claim",
                ReclaimedUtc = DateTimeOffset.UtcNow,
                AbandonedBeforeUtc =
                    DateTimeOffset.UtcNow.AddMinutes(-5),
                ProtectionClassificationId =
                    new("internal")
            };

        await Assert.ThrowsAnyAsync<Exception>(
            () => service.RecoverAsync(request));

        Assert.Equal(
            SecurityAuditOutcome.Failed,
            Assert.Single(audit.Records).Outcome);
    }

    private sealed class ThrowingSecurityAuditSink :
        ISecurityAuditSink
    {
        private readonly Exception _exception;

        public ThrowingSecurityAuditSink(Exception exception)
        {
            _exception = exception;
        }

        public Task WriteAsync(
            SecurityAuditRecord record,
            CancellationToken cancellationToken = default)
        {
            throw _exception;
        }
    }

    private sealed class AlwaysAllowAuthorizationPolicy :
        IAuthorizationPolicy
    {
        public Task<AuthorizationDecision> EvaluateAsync(
            AuthorizationRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                AuthorizationDecision.Allow);
        }
    }

    [Fact]
    public async Task Cancelled_recovery_is_audited()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-cancelled-{Guid.NewGuid():N}.db");

        try
        {
            var repository =
                new SqliteWorkflowRepository(path);

            await repository.InitializeAsync();

            var audit = new RecordingSecurityAuditSink();

            var service =
                new WorkflowActivityClaimRecoveryService(
                    repository,
                    new AlwaysAllowAuthorizationPolicy(),
                    audit);

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => service.RecoverAsync(
                    new WorkflowActivityClaimRecoveryRequest
                    {
                        SubjectId = "steward",
                        WorkflowId = new("workflow-cancelled"),
                        ActivityId = "activity",
                        NewClaimId = "new-claim",
                        ReclaimedUtc = DateTimeOffset.UtcNow,
                        AbandonedBeforeUtc =
                            DateTimeOffset.UtcNow.AddMinutes(-5),
                        ProtectionClassificationId =
                            new("internal")
                    },
                    cancellation.Token));

            Assert.Equal(
                SecurityAuditOutcome.Cancelled,
                Assert.Single(audit.Records).Outcome);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }


    [Fact]
    public async Task Future_abandonment_cutoff_is_rejected()
    {
        var audit = new RecordingSecurityAuditSink();

        var service =
            new WorkflowActivityClaimRecoveryService(
                new SqliteWorkflowRepository("unused.db"),
                new AlwaysAllowAuthorizationPolicy(),
                audit);

        var now = DateTimeOffset.UtcNow;

        var request = new WorkflowActivityClaimRecoveryRequest
        {
            SubjectId = "steward",
            WorkflowId = new("workflow-invalid"),
            ActivityId = "activity",
            NewClaimId = "new-claim",
            ReclaimedUtc = now,
            AbandonedBeforeUtc = now.AddMinutes(1),
            ProtectionClassificationId = new("internal")
        };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.RecoverAsync(request));

        Assert.Empty(audit.Records);
    }
}
