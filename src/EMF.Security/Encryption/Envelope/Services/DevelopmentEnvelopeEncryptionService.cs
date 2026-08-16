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

    public Task<EncryptedEnvelope> EncryptAsync(
        ReadOnlyMemory<byte> plaintext,
        CancellationToken cancellationToken = default) =>
        EncryptCoreAsync(plaintext, null, cancellationToken);

    public Task<EncryptedEnvelope> EncryptWithContextAsync(
        ReadOnlyMemory<byte> plaintext,
        ReadOnlyMemory<byte> authenticatedContext,
        CancellationToken cancellationToken = default) =>
        EncryptCoreAsync(
            plaintext,
            authenticatedContext,
            cancellationToken);

    private async Task<EncryptedEnvelope> EncryptCoreAsync(
        ReadOnlyMemory<byte> plaintext,
        ReadOnlyMemory<byte>? authenticatedContext,
        CancellationToken cancellationToken)
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
            authenticatedContext.HasValue
                ? EncryptedEnvelopeFormat
                    .GetContextBoundAuthenticatedData(
                        EncryptedEnvelopeFormat
                            .Aes256GcmAlgorithm,
                        authenticatedContext.Value)
                : EncryptedEnvelopeFormat.GetAuthenticatedData(
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
                authenticatedContext.HasValue
                    ? EncryptedEnvelopeFormat.ContextBoundVersion
                    : EncryptedEnvelopeFormat.CurrentVersion,
            Ciphertext = ciphertext,
            Nonce = nonce,
            AuthenticationTag = tag,
            WrappedDataEncryptionKey = wrappedDek,
            KeyEncryptionKeyId = key.KeyId,
            Algorithm =
                EncryptedEnvelopeFormat.Aes256GcmAlgorithm
        };
    }

    public Task<byte[]> DecryptAsync(
        EncryptedEnvelope envelope,
        CancellationToken cancellationToken = default) =>
        DecryptCoreAsync(envelope, null, cancellationToken);

    public Task<byte[]> DecryptWithContextAsync(
        EncryptedEnvelope envelope,
        ReadOnlyMemory<byte> authenticatedContext,
        CancellationToken cancellationToken = default) =>
        DecryptCoreAsync(
            envelope,
            authenticatedContext,
            cancellationToken);

    private async Task<byte[]> DecryptCoreAsync(
        EncryptedEnvelope envelope,
        ReadOnlyMemory<byte>? authenticatedContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var authenticatedData =
            envelope.FormatVersion ==
                EncryptedEnvelopeFormat.ContextBoundVersion
                ? EncryptedEnvelopeFormat
                    .GetContextBoundAuthenticatedData(
                        envelope.Algorithm,
                        authenticatedContext
                            ?? throw new CryptographicException(
                                "Authenticated context is required."))
                : EncryptedEnvelopeFormat.GetAuthenticatedData(
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
