using Azure.Security.KeyVault.Keys.Cryptography;

namespace EMF.Security.Azure.Cryptography;

public sealed class AzureKeyVaultCryptography :
    IAzureKeyCryptography
{
    private readonly CryptographyClient _client;

    public AzureKeyVaultCryptography(
        CryptographyClient client)
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

        var result =
            await _client.WrapKeyAsync(
                KeyWrapAlgorithm.RsaOaep256,
                key,
                cancellationToken);

        return result.EncryptedKey;
    }

    public async Task<byte[]> UnwrapKeyAsync(
        byte[] wrappedKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wrappedKey);

        cancellationToken.ThrowIfCancellationRequested();

        var result =
            await _client.UnwrapKeyAsync(
                KeyWrapAlgorithm.RsaOaep256,
                wrappedKey,
                cancellationToken);

        return result.Key;
    }
}
