using System.Security.Cryptography;
using EMF.Security.Azure.Cryptography;
using EMF.Security.Azure.Keys;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Models;

namespace EMF.Security.Azure.Encryption;

public sealed class AzureEnvelopeEncryptionService :
    IEnvelopeEncryptionService
{
    private readonly IAzureKeyReferenceProvider _keyProvider;
    private readonly IAzureKeyCryptographyFactory _cryptographyFactory;

    public AzureEnvelopeEncryptionService(
        IAzureKeyReferenceProvider keyProvider,
        IAzureKeyCryptographyFactory cryptographyFactory)
    {
        ArgumentNullException.ThrowIfNull(keyProvider);
        ArgumentNullException.ThrowIfNull(cryptographyFactory);

        _keyProvider = keyProvider;
        _cryptographyFactory = cryptographyFactory;
    }

    public async Task<EncryptedEnvelope> EncryptAsync(
        ReadOnlyMemory<byte> plaintext,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var keyReference =
            await _keyProvider.GetCurrentKeyAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(keyReference.KeyVersion))
            throw new CryptographicException("Key version is required.");

        var dek = RandomNumberGenerator.GetBytes(32);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        try
        {
            using var aes = new AesGcm(dek, 16);
            aes.Encrypt(nonce, plaintext.Span, ciphertext, tag);

            var cryptography =
                _cryptographyFactory.Create(keyReference);

            var wrappedDek =
                await cryptography.WrapKeyAsync(dek, cancellationToken);

            return new EncryptedEnvelope
            {
                Ciphertext = ciphertext,
                Nonce = nonce,
                AuthenticationTag = tag,
                WrappedDataEncryptionKey = wrappedDek,
                KeyEncryptionKeyId =
                    $"{keyReference.KeyName}/{keyReference.KeyVersion}",
                Algorithm = "AES-256-GCM"
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    public async Task<byte[]> DecryptAsync(
        EncryptedEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();

        var parts = envelope.KeyEncryptionKeyId.Split('/', 2);

        if (parts.Length != 2)
            throw new CryptographicException("Invalid key identifier.");

        var keyReference = await _keyProvider.GetKeyAsync(
            envelope.KeyEncryptionKeyId,
            cancellationToken);

        if (keyReference is null)
            throw new CryptographicException("Encryption key not found.");

        var cryptography =
            _cryptographyFactory.Create(keyReference);

        var dek = await cryptography.UnwrapKeyAsync(
            envelope.WrappedDataEncryptionKey,
            cancellationToken);

        try
        {
            var plaintext = new byte[envelope.Ciphertext.Length];

            using var aes = new AesGcm(dek, 16);
            aes.Decrypt(
                envelope.Nonce,
                envelope.Ciphertext,
                envelope.AuthenticationTag,
                plaintext);

            return plaintext;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }
}
