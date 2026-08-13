using Azure.Security.KeyVault.Keys.Cryptography;

namespace EMF.Security.Azure.Cryptography;

public sealed class AzureKeyVaultCryptography :
    IAzureKeyCryptography
{
    private readonly IAzureCryptographyClient _client;

    public AzureKeyVaultCryptography(
        IAzureCryptographyClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
    }

    public async Task<byte[]> WrapKeyAsync(
        byte[] key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        cancellationToken.ThrowIfCancellationRequested();

        return await _client.WrapKeyAsync(
            KeyWrapAlgorithm.RsaOaep256,
            key,
            cancellationToken);
    }

    public async Task<byte[]> UnwrapKeyAsync(
        byte[] wrappedKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wrappedKey);

        cancellationToken.ThrowIfCancellationRequested();

        return await _client.UnwrapKeyAsync(
            KeyWrapAlgorithm.RsaOaep256,
            wrappedKey,
            cancellationToken);
    }
}
