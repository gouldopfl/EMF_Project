using EMF.Security.Encryption.Models;

namespace EMF.Security.Encryption;

public interface IEncryptionKeyProvider
{
    Task<string?> GetCurrentKeyIdAsync(
        CancellationToken cancellationToken = default);

    Task<EncryptionKey?> GetKeyAsync(
        string keyId,
        CancellationToken cancellationToken = default);
}
