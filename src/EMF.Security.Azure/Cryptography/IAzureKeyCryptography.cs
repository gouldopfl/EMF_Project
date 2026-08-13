namespace EMF.Security.Azure.Cryptography;

public interface IAzureKeyCryptography
{
    Task<byte[]> WrapKeyAsync(
        byte[] key,
        CancellationToken cancellationToken = default);

    Task<byte[]> UnwrapKeyAsync(
        byte[] wrappedKey,
        CancellationToken cancellationToken = default);
}
