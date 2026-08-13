namespace EMF.Security.Encryption;

public interface IEncryptionKeyProvider
{
    Task<string> GetCurrentKeyIdAsync(
        CancellationToken cancellationToken = default);
}
