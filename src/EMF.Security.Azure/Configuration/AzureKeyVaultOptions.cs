namespace EMF.Security.Azure.Configuration;

public sealed class AzureKeyVaultOptions
{
    public required string VaultUri { get; init; }

    public string? KeyName { get; init; }

    public string? KeyVersion { get; init; }
}
