using EMF.Security.Auditing.Models;
using EMF.Security.Persistence.Sqlite.Auditing;

namespace EMF.Tests;

public sealed class SqliteSecurityAuditRecordReaderTests
{
    [Fact]
    public async Task FindByFactAsync_ReturnsMatchingRecord()
    {
        var path =
            Path.Combine(
                Path.GetTempPath(),
                $"{Guid.NewGuid():N}.db");

        try
        {
            var sink =
                new SqliteSecurityAuditSink(path);

            await sink.InitializeAsync();

            await sink.WriteAsync(
                new SecurityAuditRecord
                {
                    Operation =
                        "IntelligenceCapability.Execute",
                    ResourceType =
                        "IntelligenceCapability",
                    ResourceId =
                        "text.structured.extract",
                    SubjectId = "reviewer",
                    Destination = "test-provider",
                    Outcome =
                        SecurityAuditOutcome.Succeeded,
                    OccurredUtc =
                        DateTimeOffset.UtcNow,
                    Facts =
                        new Dictionary<string, string>
                        {
                            ["correlationId"] =
                                "correlation-1",
                            ["engineName"] =
                                "test-engine"
                        }
                });

            var records =
                await new SqliteSecurityAuditRecordReader(
                        path)
                    .FindByFactAsync(
                        "IntelligenceCapability.Execute",
                        "IntelligenceCapability",
                        "text.structured.extract",
                        "correlationId",
                        "correlation-1");

            var record =
                Assert.Single(records);

            Assert.Equal(
                "text.structured.extract",
                record.ResourceId);
            Assert.Equal(
                "reviewer",
                record.SubjectId);
            Assert.Equal(
                "test-provider",
                record.Destination);
            Assert.Equal(
                "Succeeded",
                record.Outcome);
            Assert.Contains(
                "correlation-1",
                record.FactsJson,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
