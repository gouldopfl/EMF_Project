using EMF.ConsoleApplication;
using EMF.Security.Auditing.Models;
using EMF.Security.Persistence.Sqlite.Auditing;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class SecurityConsoleCommandTests
{
    [Fact]
    public async Task AuditVerify_reports_integrity_status()
    {
        var root =
            Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid().ToString());

        Directory.CreateDirectory(root);

        try
        {
            var databasePath =
                Path.Combine(root, "security-command.db");

            var sink =
                new SqliteSecurityAuditSink(
                    databasePath);

            await sink.InitializeAsync();

            await sink.WriteAsync(
                new SecurityAuditRecord
                {
                    Operation = "artifact.read",
                    ResourceType = "Artifact",
                    ResourceId = "artifact-001",
                    SubjectId = "console-test",
                    Outcome =
                        SecurityAuditOutcome.Succeeded,
                    OccurredUtc =
                        new DateTimeOffset(
                            2026,
                            8,
                            16,
                            23,
                            0,
                            0,
                            TimeSpan.Zero)
                });

            var validExitCode =
                await SecurityConsoleCommand.RunAsync(
                    ["audit", "verify", databasePath]);

            Assert.Equal(0, validExitCode);

            var reportExitCode =
                await SecurityConsoleCommand.RunAsync(
                    ["audit", "report", databasePath,
                     "artifact.read"]);

            Assert.Equal(0, reportExitCode);

            await using (
                var connection =
                    new SqliteConnection(
                        $"Data Source={databasePath}"))
            {
                await connection.OpenAsync();

                await using var command =
                    connection.CreateCommand();

                command.CommandText =
                    """
                    UPDATE SecurityAuditRecords
                    SET SubjectId = 'tampered';
                    """;

                await command.ExecuteNonQueryAsync();
            }

            var invalidExitCode =
                await SecurityConsoleCommand.RunAsync(
                    ["audit", "verify", databasePath]);

            Assert.Equal(1, invalidExitCode);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
