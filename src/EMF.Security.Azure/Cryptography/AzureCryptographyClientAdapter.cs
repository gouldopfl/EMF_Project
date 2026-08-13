using Azure.Security.KeyVault.Keys.Cryptography;

namespace EMF.Security.Azure.Cryptography;

public sealed class AzureCryptographyClientAdapter :
    IAzureCryptographyClient
{
    private readonly CryptographyClient _client;

    public AzureCryptographyClientAdapter(
        CryptographyClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
    }

    public async Task<byte[]> WrapKeyAsync(
        KeyWrapAlgorithm algorithm,
        byte[] key,
        CancellationToken cancellationToken = default)
    {
        var result =
            await _client.WrapKeyAsync(
                algorithm,
                key,
                cancellationToken);

        return result.EncryptedKey;
    }

    public async Task<byte[]> UnwrapKeyAsync(
        KeyWrapAlgorithm algorithm,
        byte[] wrappedKey,
        CancellationToken cancellationToken = default)
    {
        var result =
            await _client.UnwrapKeyAsync(
                algorithm,
                wrappedKey,
                cancellationToken);

        return result.Key;
    }
}
