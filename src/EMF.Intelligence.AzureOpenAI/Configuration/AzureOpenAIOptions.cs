namespace EMF.Intelligence.AzureOpenAI.Configuration;

public sealed class AzureOpenAIOptions
{
    public required string Endpoint { get; init; }

    public required string DeploymentName { get; init; }

    public required string ProviderId { get; init; }

    public string? ManagedIdentityClientId { get; init; }

    public TimeSpan RequestTimeout { get; init; }
        = TimeSpan.FromMinutes(2);

    public int MaximumRetries { get; init; } = 2;
}
