using EMF.Security.Encryption.Providers;
using EMF.Security.Encryption.Providers.Models;

namespace EMF.Security.Encryption.Services;

public sealed class DevelopmentEncryptionService :
    IContentEncryptionService
{
    private readonly IContentCryptographyProvider _cryptographyProvider;

    public DevelopmentEncryptionService(
        IContentCryptographyProvider cryptographyProvider)
    {
        ArgumentNullException.ThrowIfNull(cryptographyProvider);

        _cryptographyProvider = cryptographyProvider;
    }

    public async Task<EncryptedContent> EncryptAsync(
        ReadOnlyMemory<byte> plaintext,
        CancellationToken cancellationToken = default)
    {
        var result =
            await _cryptographyProvider.EncryptAsync(
                plaintext,
                cancellationToken);

        return new EncryptedContent
        {
            Ciphertext = result.Ciphertext,
            Nonce = result.Nonce,
            AuthenticationTag = result.AuthenticationTag,
            KeyId = result.KeyId
        };
    }

    public async Task<byte[]> DecryptAsync(
        EncryptedContent encryptedContent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(encryptedContent);

        return await _cryptographyProvider.DecryptAsync(
            new ContentDecryptionRequest
            {
                Ciphertext = encryptedContent.Ciphertext,
                Nonce = encryptedContent.Nonce,
                AuthenticationTag = encryptedContent.AuthenticationTag,
                KeyId = encryptedContent.KeyId
            },
            cancellationToken);
    }
}
