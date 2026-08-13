using System.Security.Cryptography;

namespace EMF.Security.Encryption.Services;

public sealed class DevelopmentEncryptionService :
    IContentEncryptionService
{
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;
    private readonly string _keyId;

    public DevelopmentEncryptionService(
        byte[] key,
        string keyId)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length != KeySize)
        {
            throw new ArgumentException(
                "Key must be 32 bytes.",
                nameof(key));
        }

        if (string.IsNullOrWhiteSpace(keyId))
        {
            throw new ArgumentException(
                "Key identifier is required.",
                nameof(keyId));
        }

        _key = key.ToArray();
        _keyId = keyId;
    }

    public Task<EncryptedContent> EncryptAsync(
        ReadOnlyMemory<byte> plaintext,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);

        aes.Encrypt(
            nonce,
            plaintext.Span,
            ciphertext,
            tag);

        return Task.FromResult(
            new EncryptedContent
            {
                Ciphertext = ciphertext,
                Nonce = nonce,
                AuthenticationTag = tag,
                KeyId = _keyId
            });
    }

    public Task<byte[]> DecryptAsync(
        EncryptedContent encryptedContent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(encryptedContent);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(
                encryptedContent.KeyId,
                _keyId,
                StringComparison.Ordinal))
        {
            throw new CryptographicException(
                "Unsupported encryption key.");
        }

        var plaintext =
            new byte[encryptedContent.Ciphertext.Length];

        using var aes = new AesGcm(_key, TagSize);

        aes.Decrypt(
            encryptedContent.Nonce,
            encryptedContent.Ciphertext,
            encryptedContent.AuthenticationTag,
            plaintext);

        return Task.FromResult(plaintext);
    }
}
