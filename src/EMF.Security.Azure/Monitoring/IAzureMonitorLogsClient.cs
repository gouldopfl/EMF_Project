namespace EMF.Security.Azure.Monitoring;

public interface IAzureMonitorLogsClient
{
    Task UploadAsync(
        string ruleId,
        string streamName,
        BinaryData data,
        CancellationToken cancellationToken = default);
}
