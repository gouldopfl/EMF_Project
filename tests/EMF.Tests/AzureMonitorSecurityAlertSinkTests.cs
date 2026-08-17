using System.Text.Json;
using EMF.Security.Azure.Configuration;
using EMF.Security.Azure.Monitoring;
using EMF.Security.Monitoring;

namespace EMF.Tests;

public sealed class AzureMonitorSecurityAlertSinkTests
{
    [Fact]
    public void Constructor_rejects_insecure_endpoint()
    {
        var options = new AzureMonitorAlertOptions
        {
            Endpoint = "http://example.com",
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
                Endpoint = "https://example.monitor.azure.com",
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
    public async Task Delivery_failure_propagates()
    {
        var options = new AzureMonitorAlertOptions
        {
            Endpoint = "https://example.monitor.azure.com",
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

