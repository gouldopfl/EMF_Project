using Azure.Monitor.Ingestion;

namespace EMF.Security.Azure.Monitoring;

internal sealed class AzureMonitorLogsClient :
    IAzureMonitorLogsClient
{
    private readonly LogsIngestionClient _client;

    public AzureMonitorLogsClient(
        LogsIngestionClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
    }

    public async Task UploadAsync(
        string ruleId,
        string streamName,
        BinaryData data,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(streamName);
        ArgumentNullException.ThrowIfNull(data);

        await _client.UploadAsync(
            ruleId,
            streamName,
            new[] { data },
            cancellationToken: cancellationToken);
    }
}
