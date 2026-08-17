namespace EMF.Security.Azure.Configuration;

public sealed class AzureMonitorAlertOptions
{
    public required string Endpoint { get; init; }

    public required string RuleId { get; init; }

    public required string StreamName { get; init; }

    public string? ManagedIdentityClientId { get; init; }
}
