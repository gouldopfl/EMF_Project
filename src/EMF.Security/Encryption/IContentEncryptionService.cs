namespace EMF.Security.Encryption;

public interface IContentEncryptionService
{
    Task<EncryptedContent> EncryptAsync(
        ReadOnlyMemory<byte> plaintext,
        CancellationToken cancellationToken = default);

    Task<byte[]> DecryptAsync(
        EncryptedContent encryptedContent,
        CancellationToken cancellationToken = default);
}
