using System.Security.Cryptography;

namespace EMF.Security.Encryption.Services;

public sealed class DevelopmentEncryptionService :
    IContentEncryptionService
{
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly IEncryptionKeyProvider _keyProvider;

    public DevelopmentEncryptionService(
        IEncryptionKeyProvider keyProvider)
    {
        ArgumentNullException.ThrowIfNull(keyProvider);

        _keyProvider = keyProvider;
    }

    public async Task<EncryptedContent> EncryptAsync(
        ReadOnlyMemory<byte> plaintext,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var keyId =
            await _keyProvider.GetCurrentKeyIdAsync(
                cancellationToken);

        if (string.IsNullOrWhiteSpace(keyId))
        {
            throw new CryptographicException(
                "No current encryption key is available.");
        }

        var key =
            await _keyProvider.GetKeyAsync(
                keyId,
                cancellationToken);

        if (key is null)
        {
            throw new CryptographicException(
                "The current encryption key could not be retrieved.");
        }

        if (key.KeyMaterial.Length != KeySize)
        {
            throw new CryptographicException(
                "The encryption key must be 32 bytes.");
        }

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key.KeyMaterial, TagSize);

        aes.Encrypt(
            nonce,
            plaintext.Span,
            ciphertext,
            tag);

        return new EncryptedContent
        {
            Ciphertext = ciphertext,
            Nonce = nonce,
            AuthenticationTag = tag,
            KeyId = key.KeyId
        };
    }

    public async Task<byte[]> DecryptAsync(
        EncryptedContent encryptedContent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(encryptedContent);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(encryptedContent.KeyId))
        {
            throw new CryptographicException(
                "Encryption key identifier is required.");
        }

        var key =
            await _keyProvider.GetKeyAsync(
                encryptedContent.KeyId,
                cancellationToken);

        if (key is null)
        {
            throw new CryptographicException(
                "Unsupported encryption key.");
        }

        if (key.KeyMaterial.Length != KeySize)
        {
            throw new CryptographicException(
                "The encryption key must be 32 bytes.");
        }

        var plaintext =
            new byte[encryptedContent.Ciphertext.Length];

        using var aes =
            new AesGcm(key.KeyMaterial, TagSize);

        aes.Decrypt(
            encryptedContent.Nonce,
            encryptedContent.Ciphertext,
            encryptedContent.AuthenticationTag,
            plaintext);

        return plaintext;
    }
}
