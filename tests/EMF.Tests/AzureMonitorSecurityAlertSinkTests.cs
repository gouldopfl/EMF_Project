using System.Text.Json;
using EMF.Security.Azure.Configuration;
using EMF.Security.Azure.Monitoring;
using EMF.Security.Monitoring;

namespace EMF.Tests;

public sealed class AzureMonitorSecurityAlertSinkTests
{
    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com")]
    public void Constructor_rejects_untrusted_endpoint(
        string endpoint)
    {
        var options = new AzureMonitorAlertOptions
        {
            Endpoint = endpoint,
            RuleId = "rule",
            StreamName = "stream"
        };

        Assert.Throws<ArgumentException>(
            () => new AzureMonitorSecurityAlertSink(
                options,
                new RecordingLogsClient()));
    }



    [Fact]
    public async Task WriteAsync_uploads_structured_alert()
    {
        var client = new RecordingLogsClient();

        var sink = new AzureMonitorSecurityAlertSink(
            new AzureMonitorAlertOptions
            {
                Endpoint = "https://example.eastus-1.ingest.monitor.azure.com",
                RuleId = "rule",
                StreamName = "stream"
            },
            client);

        await sink.WriteAsync(new SecurityAlert
        {
            AlertId = "alert-001",
            AlertType = "repeated-denials",
            Severity = SecurityAlertSeverity.High,
            Operation = "artifact.access",
            ObservedUtc = DateTimeOffset.UtcNow,
            EventCount = 3,
            WindowStartedUtc = DateTimeOffset.UtcNow
        });

        using var doc = JsonDocument.Parse(client.Data!.ToStream());

        Assert.Equal(
            "alert-001",
            doc.RootElement.GetProperty("AlertId").GetString());
    }


    [Fact]
    public async Task WriteAsync_excludes_sensitive_facts()
    {
        var client = new RecordingLogsClient();

        var sink = new AzureMonitorSecurityAlertSink(
            new AzureMonitorAlertOptions
            {
                Endpoint = "https://example.eastus-1.ingest.monitor.azure.com",
                RuleId = "rule",
                StreamName = "stream"
            },
            client);

        await sink.WriteAsync(new SecurityAlert
        {
            AlertId = "alert-002",
            AlertType = "test",
            Severity = SecurityAlertSeverity.High,
            Operation = "artifact.access",
            ObservedUtc = DateTimeOffset.UtcNow,
            EventCount = 1,
            WindowStartedUtc = DateTimeOffset.UtcNow,
            Facts = new Dictionary<string, string>
            {
                ["outcome"] = "Denied",
                ["detail"] = "Bearer do-not-send",
                ["authentication"] = "Basic do-not-send",
                ["diagnostic"] = "password=do-not-send",
                ["certificate"] =
                    "-----BEGIN PRIVATE KEY----- do-not-send",
                ["accessToken"] = "do-not-send",
                ["password"] = "do-not-send"
            }
        });

        using var doc =
            JsonDocument.Parse(client.Data!.ToStream());

        var facts = doc.RootElement.GetProperty("Facts");

        Assert.Equal(
            "Denied",
            facts.GetProperty("outcome").GetString());

        Assert.False(
            facts.TryGetProperty("detail", out _));

        Assert.False(
            facts.TryGetProperty("authentication", out _));

        Assert.False(
            facts.TryGetProperty("diagnostic", out _));

        Assert.False(
            facts.TryGetProperty("certificate", out _));

        Assert.False(
            facts.TryGetProperty("accessToken", out _));

        Assert.False(
            facts.TryGetProperty("password", out _));
    }

    [Fact]
    public async Task Delivery_failure_propagates()
    {
        var options = new AzureMonitorAlertOptions
        {
            Endpoint = "https://example.eastus-1.ingest.monitor.azure.com",
            RuleId = "rule",
            StreamName = "stream"
        };

        var sink = new AzureMonitorSecurityAlertSink(
            options,
            new ThrowingLogsClient());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sink.WriteAsync(new SecurityAlert
            {
                AlertId = "alert-001",
                AlertType = "test",
                Severity = SecurityAlertSeverity.High,
                Operation = "artifact.access",
                ObservedUtc = DateTimeOffset.UtcNow,
                EventCount = 1,
                WindowStartedUtc = DateTimeOffset.UtcNow
            }));
    }

    private sealed class ThrowingLogsClient :
        IAzureMonitorLogsClient
    {
        public Task UploadAsync(
            string ruleId,
            string streamName,
            BinaryData data,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(
                "Azure Monitor delivery failed.");
        }
    }

    private sealed class RecordingLogsClient :
        IAzureMonitorLogsClient
    {
        public BinaryData? Data { get; private set; }

        public Task UploadAsync(
            string ruleId,
            string streamName,
            BinaryData data,
            CancellationToken cancellationToken = default)
        {
            Data = data;
            return Task.CompletedTask;
        }
    }
}

