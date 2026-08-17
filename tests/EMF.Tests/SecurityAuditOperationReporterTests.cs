using Microsoft.Data.Sqlite;
using EMF.Security.Auditing.Models;
using EMF.Security.Persistence.Sqlite.Auditing;

namespace EMF.Tests;

public sealed class SecurityAuditOperationReporterTests
{
    [Fact]
    public async Task Report_groups_verified_operation_outcomes()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-audit-report-{Guid.NewGuid():N}.db");

        try
        {
            var sink = new SqliteSecurityAuditSink(path);
            await sink.InitializeAsync();

            var occurred =
                new DateTimeOffset(
                    2026, 8, 17, 12, 0, 0,
                    TimeSpan.Zero);

            foreach (var outcome in new[]
            {
                SecurityAuditOutcome.Succeeded,
                SecurityAuditOutcome.Denied,
                SecurityAuditOutcome.Denied
            })
            {
                await sink.WriteAsync(
                    new SecurityAuditRecord
                    {
                        Operation =
                            "workflow.activity-claim.recover",
                        ResourceType = "Workflow",
                        ResourceId = "workflow-001",
                        SubjectId = "steward",
                        Outcome = outcome,
                        OccurredUtc = occurred
                    });

                occurred = occurred.AddMinutes(1);
            }

            var report =
                await new
                    SqliteSecurityAuditOperationReporter(path)
                    .CreateAsync(
                        "workflow.activity-claim.recover");

            Assert.Equal(3, report.TotalCount);
            Assert.Equal(
                1,
                report.OutcomeCounts[
                    SecurityAuditOutcome.Succeeded]);
            Assert.Equal(
                2,
                report.OutcomeCounts[
                    SecurityAuditOutcome.Denied]);
            Assert.NotNull(report.FirstOccurredUtc);
            Assert.NotNull(report.LastOccurredUtc);
            Assert.NotNull(report.ChainHeadHash);
            Assert.Equal(64, report.ChainHeadHash!.Length);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task Report_refuses_tampered_audit_chain()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-tampered-report-{Guid.NewGuid():N}.db");

        try
        {
            var sink = new SqliteSecurityAuditSink(path);
            await sink.InitializeAsync();

            await sink.WriteAsync(
                new SecurityAuditRecord
                {
                    Operation =
                        "workflow.activity-claim.recover",
                    ResourceType = "Workflow",
                    ResourceId = "workflow-001",
                    SubjectId = "steward",
                    Outcome =
                        SecurityAuditOutcome.Succeeded,
                    OccurredUtc = DateTimeOffset.UtcNow
                });

            await using var connection =
                new SqliteConnection(
                    $"Data Source={path}");

            await connection.OpenAsync();

            await using var command =
                connection.CreateCommand();

            command.CommandText =
                """
                UPDATE SecurityAuditRecords
                SET SubjectId = 'tampered';
                """;

            await command.ExecuteNonQueryAsync();

            var reporter =
                new SqliteSecurityAuditOperationReporter(
                    path);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => reporter.CreateAsync(
                    "workflow.activity-claim.recover"));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
