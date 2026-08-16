using System.Security.Cryptography;
using EMF.Security.Encryption.Envelope.Models;

namespace EMF.Security.Encryption.Envelope.Services;

public sealed class DevelopmentEnvelopeEncryptionService :
    IEnvelopeEncryptionService
{
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly IEncryptionKeyProvider _keyProvider;

    public DevelopmentEnvelopeEncryptionService(
        IEncryptionKeyProvider keyProvider)
    {
        ArgumentNullException.ThrowIfNull(keyProvider);
        _keyProvider = keyProvider;
    }

    public async Task<EncryptedEnvelope> EncryptAsync(
        ReadOnlyMemory<byte> plaintext,
        CancellationToken cancellationToken = default)
    {
        var keyId =
            await _keyProvider.GetCurrentKeyIdAsync(
                cancellationToken);

        if (string.IsNullOrWhiteSpace(keyId))
            throw new CryptographicException("No current key.");

        var key =
            await _keyProvider.GetKeyAsync(
                keyId,
                cancellationToken);

        if (key is null || key.KeyMaterial.Length != KeySize)
            throw new CryptographicException("Invalid encryption key.");

        var dek = RandomNumberGenerator.GetBytes(KeySize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        var authenticatedData =
            EncryptedEnvelopeFormat.GetAuthenticatedData(
                EncryptedEnvelopeFormat.CurrentVersion,
                EncryptedEnvelopeFormat.Aes256GcmAlgorithm);

        using (var aes = new AesGcm(dek, TagSize))
        {
            aes.Encrypt(
                nonce,
                plaintext.Span,
                ciphertext,
                tag,
                authenticatedData);
        }

        var wrappedDek =
            DevelopmentDataEncryptionKeyWrapper.Wrap(key.KeyMaterial, dek);

        CryptographicOperations.ZeroMemory(dek);

        return new EncryptedEnvelope
        {
            FormatVersion =
                EncryptedEnvelopeFormat.CurrentVersion,
            Ciphertext = ciphertext,
            Nonce = nonce,
            AuthenticationTag = tag,
            WrappedDataEncryptionKey = wrappedDek,
            KeyEncryptionKeyId = key.KeyId,
            Algorithm =
                EncryptedEnvelopeFormat.Aes256GcmAlgorithm
        };
    }

    public async Task<byte[]> DecryptAsync(
        EncryptedEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var authenticatedData =
            EncryptedEnvelopeFormat.GetAuthenticatedData(
                envelope.FormatVersion,
                envelope.Algorithm);

        var key =
            await _keyProvider.GetKeyAsync(
                envelope.KeyEncryptionKeyId,
                cancellationToken);

        if (key is null || key.KeyMaterial.Length != KeySize)
            throw new CryptographicException("Invalid encryption key.");

        var dek =
            DevelopmentDataEncryptionKeyWrapper.Unwrap(
                key.KeyMaterial,
                envelope.WrappedDataEncryptionKey);

        try
        {
            var plaintext =
                new byte[envelope.Ciphertext.Length];

            using var aes =
                new AesGcm(dek, TagSize);

            aes.Decrypt(
                envelope.Nonce,
                envelope.Ciphertext,
                envelope.AuthenticationTag,
                plaintext,
                authenticatedData);

            return plaintext;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

}
