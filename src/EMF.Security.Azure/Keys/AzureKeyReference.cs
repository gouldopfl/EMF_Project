namespace EMF.Security.Azure.Keys;

public sealed class AzureKeyReference
{
    public required string KeyName { get; init; }

    public string? KeyVersion { get; init; }
}
