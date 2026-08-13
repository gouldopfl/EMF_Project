namespace EMF.Security.Azure.Keys;

public interface IAzureKeyReferenceProvider
{
    Task<AzureKeyReference> GetCurrentKeyAsync(
        CancellationToken cancellationToken = default);

    Task<AzureKeyReference?> GetKeyAsync(
        string keyIdentifier,
        CancellationToken cancellationToken = default);
}
