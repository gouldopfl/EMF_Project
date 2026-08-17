using EMF.Security.Auditing.Models;
using EMF.Security.Monitoring;
using EMF.Security.Persistence.Sqlite.Auditing;

namespace EMF.Tests;

public sealed class SecurityAuditThresholdEvaluatorTests
{
    [Fact]
    public async Task Threshold_controls_alert_delivery()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-alert-{Guid.NewGuid():N}.db");

        try
        {
            var sink = new SqliteSecurityAuditSink(path);
            await sink.InitializeAsync();

            var observedUtc =
                new DateTimeOffset(
                    2026, 8, 17, 14, 0, 0,
                    TimeSpan.Zero);

            for (var index = 0; index < 3; index++)
            {
                await sink.WriteAsync(
                    new SecurityAuditRecord
                    {
                        Operation =
                            "workflow.activity-claim.recover",
                        ResourceType = "Workflow",
                        ResourceId = $"workflow-{index}",
                        SubjectId = "unknown",
                        Outcome = SecurityAuditOutcome.Denied,
                        OccurredUtc =
                            observedUtc.AddMinutes(-index)
                    });
            }

            var alerts = new RecordingAlertSink();
            var evaluator =
                new SqliteSecurityAuditThresholdEvaluator(
                    path, alerts);

            var rule = new SecurityAuditThresholdRule
            {
                AlertType = "repeated-denials",
                Operation =
                    "workflow.activity-claim.recover",
                Outcome = SecurityAuditOutcome.Denied,
                Threshold = 4,
                Window = TimeSpan.FromMinutes(10),
                Severity = SecurityAlertSeverity.High
            };

            Assert.False(await evaluator.EvaluateAsync(
                rule, observedUtc));

            rule = new SecurityAuditThresholdRule
            {
                AlertType = rule.AlertType,
                Operation = rule.Operation,
                Outcome = rule.Outcome,
                Threshold = 3,
                Window = rule.Window,
                Severity = rule.Severity
            };

            Assert.True(await evaluator.EvaluateAsync(
                rule, observedUtc));

            var alert = Assert.Single(alerts.Alerts);

            Assert.Equal(3, alert.EventCount);
            Assert.Equal(
                SecurityAlertSeverity.High,
                alert.Severity);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private sealed class RecordingAlertSink :
        ISecurityAlertSink
    {
        public List<SecurityAlert> Alerts { get; } = [];

        public Task WriteAsync(
            SecurityAlert alert,
            CancellationToken cancellationToken = default)
        {
            Alerts.Add(alert);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Alert_delivery_failure_propagates()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-alert-failure-{Guid.NewGuid():N}.db");

        try
        {
            var sink = new SqliteSecurityAuditSink(path);
            await sink.InitializeAsync();

            var observedUtc = DateTimeOffset.UtcNow;

            await sink.WriteAsync(
                new SecurityAuditRecord
                {
                    Operation = "artifact.access",
                    ResourceType = "Artifact",
                    ResourceId = "artifact-001",
                    SubjectId = "unknown",
                    Outcome = SecurityAuditOutcome.Denied,
                    OccurredUtc = observedUtc
                });

            var evaluator =
                new SqliteSecurityAuditThresholdEvaluator(
                    path,
                    new ThrowingAlertSink());

            var rule = new SecurityAuditThresholdRule
            {
                AlertType = "delivery-failure",
                Operation = "artifact.access",
                Outcome = SecurityAuditOutcome.Denied,
                Threshold = 1,
                Window = TimeSpan.FromMinutes(10),
                Severity = SecurityAlertSeverity.High
            };

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => evaluator.EvaluateAsync(
                    rule,
                    observedUtc));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private sealed class ThrowingAlertSink :
        ISecurityAlertSink
    {
        public Task WriteAsync(
            SecurityAlert alert,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(
                "Alert delivery failed.");
        }
    }
}
