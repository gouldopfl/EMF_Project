using Azure.Core;
using Azure.Identity;
using Azure.Monitor.Ingestion;
using EMF.Security.Azure.Configuration;

namespace EMF.Security.Azure.Monitoring;

public sealed class AzureMonitorLogsClientFactory
{
    private readonly AzureMonitorAlertOptions _options;
    private readonly Uri _endpoint;

    public AzureMonitorLogsClientFactory(
        AzureMonitorAlertOptions options)
    {
        _endpoint =
            AzureMonitorAlertOptionsValidator.Validate(options);

        _options = options;
    }

    public IAzureMonitorLogsClient Create()
    {
        TokenCredential credential =
            string.IsNullOrWhiteSpace(
                _options.ManagedIdentityClientId)
                ? new DefaultAzureCredential()
                : new DefaultAzureCredential(
                    new DefaultAzureCredentialOptions
                    {
                        ManagedIdentityClientId =
                            _options.ManagedIdentityClientId
                    });

        return new AzureMonitorLogsClient(
            new LogsIngestionClient(
                _endpoint,
                credential));
    }
}
