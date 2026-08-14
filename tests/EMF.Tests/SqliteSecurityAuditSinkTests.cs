using System.Text.Json;
using EMF.Security.Auditing.Models;
using EMF.Security.Authorization;
using EMF.Security.Persistence.Sqlite.Auditing;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class SqliteSecurityAuditSinkTests
{
    [Fact]
    public async Task WriteAsync_persists_security_audit_record()
    {
        var root =
            Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid().ToString());

        Directory.CreateDirectory(root);

        try
        {
            var databasePath =
                Path.Combine(root, "security-audit.db");

            var sink =
                new SqliteSecurityAuditSink(
                    databasePath);

            await sink.InitializeAsync();

            var occurredUtc =
                new DateTimeOffset(
                    2026,
                    8,
                    14,
                    12,
                    0,
                    0,
                    TimeSpan.Zero);

            await sink.WriteAsync(
                new SecurityAuditRecord
                {
                    Operation =
                        "artifact.envelope.rewrap",
                    ResourceType = "Artifact",
                    ResourceId = "artifact-001",
                    SubjectId = "security-steward",
                    PolicyDecision =
                        AuthorizationDecision.Allow,
                    Destination = "Azure Key Vault",
                    Outcome =
                        SecurityAuditOutcome.Succeeded,
                    OccurredUtc = occurredUtc,
                    Facts =
                        new Dictionary<string, string>
                        {
                            ["previousKeyEncryptionKeyId"] =
                                "key/v1",
                            ["currentKeyEncryptionKeyId"] =
                                "key/v2"
                        }
                });

            await using var connection =
                new SqliteConnection(
                    $"Data Source={databasePath}");

            await connection.OpenAsync();

            await using var command =
                connection.CreateCommand();

            command.CommandText =
                """
                SELECT
                    Operation,
                    ResourceType,
                    ResourceId,
                    SubjectId,
                    PolicyDecision,
                    Destination,
                    Outcome,
                    OccurredUtc,
                    FactsJson
                FROM SecurityAuditRecords;
                """;

            await using var reader =
                await command.ExecuteReaderAsync();

            Assert.True(await reader.ReadAsync());
            Assert.Equal(
                "artifact.envelope.rewrap",
                reader.GetString(0));
            Assert.Equal("Artifact", reader.GetString(1));
            Assert.Equal("artifact-001", reader.GetString(2));
            Assert.Equal(
                "security-steward",
                reader.GetString(3));
            Assert.Equal("Allow", reader.GetString(4));
            Assert.Equal(
                "Azure Key Vault",
                reader.GetString(5));
            Assert.Equal("Succeeded", reader.GetString(6));
            Assert.Equal(
                occurredUtc,
                DateTimeOffset.Parse(
                    reader.GetString(7)));

            var facts =
                JsonSerializer.Deserialize<
                    Dictionary<string, string>>(
                        reader.GetString(8));

            Assert.NotNull(facts);
            Assert.Equal(
                "key/v1",
                facts!["previousKeyEncryptionKeyId"]);
            Assert.Equal(
                "key/v2",
                facts["currentKeyEncryptionKeyId"]);
            Assert.False(await reader.ReadAsync());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
