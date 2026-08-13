using EMF.Security.Azure.Configuration;

namespace EMF.Security.Azure.Keys;

public sealed class ConfiguredAzureKeyReferenceProvider :
    IAzureKeyReferenceProvider
{
    private readonly AzureKeyVaultOptions _options;

    public ConfiguredAzureKeyReferenceProvider(
        AzureKeyVaultOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public Task<AzureKeyReference> GetCurrentKeyAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_options.KeyName))
            throw new InvalidOperationException("Key name is required.");

        return Task.FromResult(
            new AzureKeyReference
            {
                KeyName = _options.KeyName,
                KeyVersion = _options.KeyVersion
            });
    }

    public Task<AzureKeyReference?> GetKeyAsync(
        string keyIdentifier,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var parts = keyIdentifier.Split('/', 2);

        if (parts.Length != 2)
            return Task.FromResult<AzureKeyReference?>(null);

        return Task.FromResult<AzureKeyReference?>(
            new AzureKeyReference
            {
                KeyName = parts[0],
                KeyVersion = parts[1]
            });
    }
}
