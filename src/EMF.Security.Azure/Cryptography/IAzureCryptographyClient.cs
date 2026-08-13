using Azure.Security.KeyVault.Keys.Cryptography;

namespace EMF.Security.Azure.Cryptography;

public interface IAzureCryptographyClient
{
    Task<byte[]> WrapKeyAsync(
        KeyWrapAlgorithm algorithm,
        byte[] key,
        CancellationToken cancellationToken = default);

    Task<byte[]> UnwrapKeyAsync(
        KeyWrapAlgorithm algorithm,
        byte[] wrappedKey,
        CancellationToken cancellationToken = default);
}
