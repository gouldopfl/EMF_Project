using System.Text.Json;
using EMF.Security.Azure.Configuration;
using EMF.Security.Azure.Monitoring;
using EMF.Security.Monitoring;

namespace EMF.Tests;

public sealed class Adr032AzureAllowlistTests
{
    [Theory]
    [InlineData("audit.integrity-failure", "FailureCategory", "MissingRecordHash")]
    [InlineData("telemetry.required-missing", "ExpectationId", "expectation-1")]
    public async Task Approved_new_diagnostics_survive(string type, string key, string value)
    {
        var facts = await Forward(type, new() { [key] = value });
        Assert.Equal(value, facts.GetProperty(key).GetString());
    }

    [Theory]
    [InlineData("audit.integrity-failure", "ExpectationId", "expectation-1")]
    [InlineData("telemetry.required-missing", "FailureCategory", "MissingRecordHash")]
    [InlineData("audit.integrity-failure", "FailureCategory", "999")]
    [InlineData("audit.integrity-failure", "FirstInvalidRecordId", "-1")]
    [InlineData("audit.integrity-failure", "ProtectedPrefixCount", "-1")]
    [InlineData("audit.integrity-failure", "VerificationUtc", "private text")]
    [InlineData("telemetry.required-missing", "ExpectationId", "private text\nsecret")]
    [InlineData("telemetry.required-missing", "DeadlineUtc", "not a timestamp")]
    [InlineData("telemetry.required-missing", "Operation", "private text")]
    public async Task Invalid_or_cross_family_fields_are_removed(string type, string key, string value)
    {
        Assert.Empty((await Forward(type, new() { [key] = value })).EnumerateObject());
    }

    [Theory]
    [InlineData("audit.integrity-failure")]
    [InlineData("telemetry.required-missing")]
    public async Task Secrets_and_unapproved_fields_are_removed(string type)
    {
        var facts = await Forward(type, new()
        {
            ["Reason"] = "protected medical content",
            ["token"] = "Bearer secret",
            ["key"] = "-----BEGIN PRIVATE KEY-----",
            ["ResourceId"] = "private business",
            ["threshold"] = "1",
            ["chainHeadHash"] = new string('A', 64),
            ["MaximumSilenceSeconds"] = "NaN"
        });
        Assert.Empty(facts.EnumerateObject());
    }

    [Theory]
    [InlineData("audit.integrity-failure")]
    [InlineData("telemetry.required-missing")]
    public async Task Complete_approved_schema_survives_without_extra_facts(string type)
    {
        var utc = DateTimeOffset.UnixEpoch.ToString("O");
        Dictionary<string, string> expected = type == "audit.integrity-failure" ? new()
        {
            ["AuditSourceId"] = "source-1",
            ["FailureCategory"] = "RecordHashMismatch",
            ["FirstInvalidRecordId"] = "3",
            ["ProtectedPrefixCount"] = "1",
            ["LegacyPrefixCount"] = "0",
            ["VerificationUtc"] = utc
        } : new()
        {
            ["ExpectationId"] = "expectation-1",
            ["SourceId"] = "source-1",
            ["Operation"] = "heartbeat",
            ["EvaluationUtc"] = utc,
            ["DeadlineUtc"] = utc,
            ["LastObservedUtc"] = utc
        };
        var result = await Forward(type, expected);
        Assert.Equal(expected.Count, result.EnumerateObject().Count());
        foreach (var pair in expected) Assert.Equal(pair.Value, result.GetProperty(pair.Key).GetString());
    }

    [Theory]
    [InlineData("audit.integrity-failure", "AuditSourceId", "")]
    [InlineData("audit.integrity-failure", "FirstInvalidRecordId", "9223372036854775808")]
    [InlineData("audit.integrity-failure", "LegacyPrefixCount", "2147483648")]
    [InlineData("audit.integrity-failure", "VerificationUtc", "2026-10-01T00:00:00.0000000+01:00")]
    [InlineData("telemetry.required-missing", "SourceId", "password=secret")]
    [InlineData("telemetry.required-missing", "LastObservedUtc", "2026-10-01")]
    [InlineData("telemetry.required-missing", "MaximumSilenceSeconds", "NaN")]
    [InlineData("telemetry.required-missing", "GracePeriodSeconds", "-1")]
    public async Task Out_of_range_and_unapproved_duration_fields_are_removed(string type, string key, string value)
    {
        Assert.Empty((await Forward(type, new() { [key] = value })).EnumerateObject());
    }

    private static async Task<JsonElement> Forward(string type, Dictionary<string, string> facts)
    {
        var client = new Client();
        var sink = new AzureMonitorSecurityAlertSink(new AzureMonitorAlertOptions
        { Endpoint = "https://example.eastus-1.ingest.monitor.azure.com", RuleId = "rule", StreamName = "stream" }, client);
        await sink.WriteAsync(new SecurityAlert
        {
            AlertId = "test",
            AlertType = type,
            Operation = "security.audit.verify",
            Severity = SecurityAlertSeverity.High,
            ObservedUtc = DateTimeOffset.UnixEpoch,
            WindowStartedUtc = DateTimeOffset.UnixEpoch,
            EventCount = 1,
            Facts = facts
        });
        using var doc = JsonDocument.Parse(client.Data!.ToStream());
        return doc.RootElement.GetProperty("Facts").Clone();
    }
    private sealed class Client : IAzureMonitorLogsClient
    {
        public BinaryData? Data;
        public Task UploadAsync(string ruleId, string streamName, BinaryData data, CancellationToken cancellationToken = default)
        { Data = data; return Task.CompletedTask; }
    }
}
