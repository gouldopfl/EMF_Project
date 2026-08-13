using System.Security.Cryptography;
using EMF.Security.Encryption.Providers.Models;

namespace EMF.Security.Encryption.Providers.Services;

public sealed class DevelopmentContentCryptographyProvider :
    IContentCryptographyProvider
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly IEncryptionKeyProvider _keyProvider;

    public DevelopmentContentCryptographyProvider(
        IEncryptionKeyProvider keyProvider)
    {
        ArgumentNullException.ThrowIfNull(keyProvider);

        _keyProvider = keyProvider;
    }

    public async Task<ContentEncryptionResult> EncryptAsync(
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

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes =
            new AesGcm(key.KeyMaterial, TagSize);

        aes.Encrypt(
            nonce,
            plaintext.Span,
            ciphertext,
            tag);

        return new ContentEncryptionResult
        {
            Ciphertext = ciphertext,
            Nonce = nonce,
            AuthenticationTag = tag,
            KeyId = key.KeyId
        };
    }

    public async Task<byte[]> DecryptAsync(
        ContentDecryptionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.KeyId))
        {
            throw new CryptographicException(
                "Encryption key identifier is required.");
        }

        var key =
            await _keyProvider.GetKeyAsync(
                request.KeyId,
                cancellationToken);

        if (key is null)
        {
            throw new CryptographicException(
                "Unsupported encryption key.");
        }

        var plaintext =
            new byte[request.Ciphertext.Length];

        using var aes =
            new AesGcm(key.KeyMaterial, TagSize);

        aes.Decrypt(
            request.Nonce,
            request.Ciphertext,
            request.AuthenticationTag,
            plaintext);

        return plaintext;
    }
}
