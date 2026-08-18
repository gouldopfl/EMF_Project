using EMF.Security.Azure.Configuration;
using EMF.Security.Azure.Monitoring;
using EMF.Security.Monitoring;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class AzureMonitorSecurityAlertSinkIntegrationTests
{
    [AzureMonitorIntegrationFact]
    public async Task WriteAsync_UploadsSyntheticAlert()
    {
        var endpoint =
            Environment.GetEnvironmentVariable(
                "EMF_AZURE_MONITOR_ENDPOINT");

        var ruleId =
            Environment.GetEnvironmentVariable(
                "EMF_AZURE_MONITOR_DCR_ID");

        var streamName =
            Environment.GetEnvironmentVariable(
                "EMF_AZURE_MONITOR_STREAM");

        Assert.False(string.IsNullOrWhiteSpace(endpoint));
        Assert.False(string.IsNullOrWhiteSpace(ruleId));
        Assert.False(string.IsNullOrWhiteSpace(streamName));

        var options =
            new AzureMonitorAlertOptions
            {
                Endpoint = endpoint!,
                RuleId = ruleId!,
                StreamName = streamName!
            };

        var client =
            new AzureMonitorLogsClientFactory(options)
                .Create();

        var sink =
            new AzureMonitorSecurityAlertSink(
                options,
                client);

        var observedUtc = DateTimeOffset.UtcNow;

        await sink.WriteAsync(
            new SecurityAlert
            {
                AlertId =
                    $"integration-{Guid.NewGuid():N}",
                AlertType =
                    "azure-monitor-integration-test",
                Severity =
                    SecurityAlertSeverity.Low,
                Operation =
                    "integration.test",
                ObservedUtc = observedUtc,
                EventCount = 1,
                WindowStartedUtc =
                    observedUtc - TimeSpan.FromMinutes(1),
                Facts =
                    new Dictionary<string, string>
                    {
                        ["outcome"] = "Synthetic",
                        ["purpose"] = "integration-validation"
                    }
            });
    }
}
