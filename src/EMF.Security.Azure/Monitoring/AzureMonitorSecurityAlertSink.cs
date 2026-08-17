using System.Text.Json;
using EMF.Security.Azure.Configuration;
using EMF.Security.Monitoring;

namespace EMF.Security.Azure.Monitoring;

public sealed class AzureMonitorSecurityAlertSink :
    ISecurityAlertSink
{
    private readonly AzureMonitorAlertOptions _options;
    private readonly IAzureMonitorLogsClient _client;

    public AzureMonitorSecurityAlertSink(
        AzureMonitorAlertOptions options,
        IAzureMonitorLogsClient client)
    {
        _ = AzureMonitorAlertOptionsValidator.Validate(
            options);

        ArgumentNullException.ThrowIfNull(client);

        _options = options;
        _client = client;
    }

    public Task WriteAsync(
        SecurityAlert alert,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alert);

        var payload =
            JsonSerializer.SerializeToUtf8Bytes(
                new
                {
                    alert.AlertId,
                    alert.AlertType,
                    Severity = alert.Severity.ToString(),
                    alert.Operation,
                    alert.ObservedUtc,
                    alert.EventCount,
                    alert.WindowStartedUtc,
                    Facts = SecurityAlertFactSanitizer.Sanitize(alert.Facts)
                });

        return _client.UploadAsync(
            _options.RuleId,
            _options.StreamName,
            BinaryData.FromBytes(payload),
            cancellationToken);
    }
}
