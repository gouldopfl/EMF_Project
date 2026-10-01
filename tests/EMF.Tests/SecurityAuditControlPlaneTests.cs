using EMF.Security.Auditing.Models;
using EMF.Security.Monitoring;
using EMF.Security.Persistence.Sqlite.Auditing;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class SecurityAuditControlPlaneTests
{
    [Theory]
    [InlineData("UPDATE SecurityAuditRecords SET Id = 3 WHERE Id = 1;", 2)]
    [InlineData("UPDATE SecurityAuditRecords SET PreviousRecordHash = 'substituted' WHERE Id = 2;", 2)]
    [InlineData("PRAGMA ignore_check_constraints = ON; UPDATE SecurityAuditRecords SET IntegrityVersion = 99 WHERE Id = 2;", 2)]
    [InlineData("UPDATE SecurityAuditRecords SET IntegrityVersion = 0, PreviousRecordHash = NULL, RecordHash = NULL WHERE Id = 2;", 2)]
    [InlineData("UPDATE SecurityAuditRecords SET RecordHash = NULL WHERE Id = 2;", 2)]
    public async Task Verifier_rejects_corrupt_chain(string mutation, long invalidId)
    {
        var path = Path.Combine(Path.GetTempPath(), $"emf-control-{Guid.NewGuid():N}.db");
        try
        {
            var sink = new SqliteSecurityAuditSink(path);
            await sink.InitializeAsync();
            await sink.WriteAsync(CreateRecord("first"));
            await sink.WriteAsync(CreateRecord("second"));
            await MutateAsync(path, mutation);

            var result = await new SqliteSecurityAuditIntegrityVerifier(path).VerifyAsync();
            Assert.False(result.IsValid);
            Assert.Equal(invalidId, result.InvalidRecordId);
            Assert.False(string.IsNullOrWhiteSpace(result.FailureReason));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("PRAGMA ignore_check_constraints = ON; UPDATE SecurityAuditRecords SET IntegrityVersion = 99 WHERE Id = 2;")]
    [InlineData("UPDATE SecurityAuditRecords SET RecordHash = NULL WHERE Id = 2;")]
    [InlineData("UPDATE SecurityAuditRecords SET IntegrityVersion = 0, PreviousRecordHash = NULL, RecordHash = NULL WHERE Id = 2;")]
    public async Task Writer_refuses_invalid_chain_head(string mutation)
    {
        var path = Path.Combine(Path.GetTempPath(), $"emf-control-{Guid.NewGuid():N}.db");
        try
        {
            var sink = new SqliteSecurityAuditSink(path);
            await sink.InitializeAsync();
            await sink.WriteAsync(CreateRecord("first"));
            await sink.WriteAsync(CreateRecord("second"));
            await MutateAsync(path, mutation);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => sink.WriteAsync(CreateRecord("third")));

            await using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM SecurityAuditRecords;";
            Assert.Equal(2L, await command.ExecuteScalarAsync());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Unprotected_legacy_evidence_does_not_trigger_alerts()
    {
        var path = Path.Combine(Path.GetTempPath(), $"emf-control-{Guid.NewGuid():N}.db");
        try
        {
            var sink = new SqliteSecurityAuditSink(path);
            await sink.InitializeAsync();
            await sink.WriteAsync(CreateRecord("legacy"));
            await MutateAsync(path,
                "UPDATE SecurityAuditRecords SET IntegrityVersion = 0, PreviousRecordHash = NULL, RecordHash = NULL;");
            var integrity = await new SqliteSecurityAuditIntegrityVerifier(path).VerifyAsync();
            Assert.True(integrity.IsValid);
            Assert.Equal(1, integrity.LegacyRecordCount);
            Assert.Equal(0, integrity.ProtectedRecordCount);
            var alerts = new CountingAlertSink();
            var evaluator = new SqliteSecurityAuditThresholdEvaluator(path, alerts);

            Assert.False(await evaluator.EvaluateAsync(new SecurityAuditThresholdRule
            {
                AlertType = "denials", Operation = "artifact.access",
                Outcome = SecurityAuditOutcome.Denied, Threshold = 1,
                Window = TimeSpan.FromHours(1), Severity = SecurityAlertSeverity.High
            }, DateTimeOffset.UtcNow));
            Assert.Equal(0, alerts.Count);

            await sink.WriteAsync(CreateRecord("protected"));
            Assert.True(await evaluator.EvaluateAsync(new SecurityAuditThresholdRule
            {
                AlertType = "denials", Operation = "artifact.access",
                Outcome = SecurityAuditOutcome.Denied, Threshold = 1,
                Window = TimeSpan.FromHours(1), Severity = SecurityAlertSeverity.High
            }, DateTimeOffset.UtcNow));
            Assert.Equal(1, alerts.Count);
            var report = await new SqliteSecurityAuditOperationReporter(path).CreateAsync("artifact.access");
            Assert.Equal(1, report.TotalCount);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Tampered_evidence_does_not_reach_alert_sink()
    {
        var path = Path.Combine(Path.GetTempPath(), $"emf-control-{Guid.NewGuid():N}.db");
        try
        {
            var sink = new SqliteSecurityAuditSink(path);
            await sink.InitializeAsync();
            await sink.WriteAsync(CreateRecord("first"));
            await MutateAsync(path, "UPDATE SecurityAuditRecords SET SubjectId = 'tampered';");
            var alerts = new CountingAlertSink();
            var evaluator = new SqliteSecurityAuditThresholdEvaluator(path, alerts);

            await Assert.ThrowsAsync<InvalidOperationException>(() => evaluator.EvaluateAsync(
                new SecurityAuditThresholdRule
                {
                    AlertType = "denials", Operation = "artifact.access",
                    Outcome = SecurityAuditOutcome.Denied, Threshold = 1,
                    Window = TimeSpan.FromHours(1), Severity = SecurityAlertSeverity.High
                }, DateTimeOffset.UtcNow));
            Assert.Equal(0, alerts.Count);
        }
        finally { File.Delete(path); }
    }

    private static SecurityAuditRecord CreateRecord(string resourceId) => new()
    {
        Operation = "artifact.access", ResourceType = "Artifact", ResourceId = resourceId,
        SubjectId = "synthetic", Outcome = SecurityAuditOutcome.Denied,
        OccurredUtc = DateTimeOffset.UtcNow
    };

    private static async Task MutateAsync(string path, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class CountingAlertSink : ISecurityAlertSink
    {
        public int Count { get; private set; }
        public Task WriteAsync(SecurityAlert alert, CancellationToken cancellationToken = default)
        {
            Count++;
            return Task.CompletedTask;
        }
    }
}
