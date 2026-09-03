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
        var clientId =
            _options.ManagedIdentityClientId;

        var managedIdentityId =
            string.IsNullOrWhiteSpace(clientId)
                ? ManagedIdentityId.SystemAssigned
                : ManagedIdentityId
                    .FromUserAssignedClientId(clientId);

        TokenCredential credential =
            new ManagedIdentityCredential(
                managedIdentityId);

        return new AzureMonitorLogsClient(
            new LogsIngestionClient(
                _endpoint,
                credential));
    }
}
