using EMF.Security.Encryption.Providers.Models;

namespace EMF.Security.Encryption.Providers;

public interface IContentCryptographyProvider
{
    Task<ContentEncryptionResult> EncryptAsync(
        ReadOnlyMemory<byte> plaintext,
        CancellationToken cancellationToken = default);

    Task<byte[]> DecryptAsync(
        ContentDecryptionRequest request,
        CancellationToken cancellationToken = default);
}
