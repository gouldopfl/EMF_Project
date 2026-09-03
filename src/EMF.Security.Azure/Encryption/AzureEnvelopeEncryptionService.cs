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
        cancellationToken.ThrowIfCancellationRequested();

        var keyReference =
            await _keyProvider.GetCurrentKeyAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(keyReference.KeyVersion))
            throw new CryptographicException("Key version is required.");

        var dek = RandomNumberGenerator.GetBytes(32);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

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

        try
        {
            using var aes = new AesGcm(dek, 16);
            aes.Encrypt(
                nonce,
                plaintext.Span,
                ciphertext,
                tag,
                authenticatedData);

            var cryptography =
                _cryptographyFactory.Create(keyReference);

            var wrappedDek =
                await cryptography.WrapKeyAsync(dek, cancellationToken);

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
                KeyEncryptionKeyId =
                    $"{keyReference.KeyName}/{keyReference.KeyVersion}",
                Algorithm =
                    EncryptedEnvelopeFormat.Aes256GcmAlgorithm
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
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
        cancellationToken.ThrowIfCancellationRequested();
        EncryptedEnvelopeFormat.Validate(envelope);

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
            if (dek.Length != 32)
            {
                throw new CryptographicException(
                    "Invalid data encryption key length.");
            }

            var plaintext = new byte[envelope.Ciphertext.Length];

            try
            {
                using var aes = new AesGcm(dek, 16);
                aes.Decrypt(
                    envelope.Nonce,
                    envelope.Ciphertext,
                    envelope.AuthenticationTag,
                    plaintext,
                    authenticatedData);

                return plaintext;
            }
            catch
            {
                CryptographicOperations.ZeroMemory(
                    plaintext);
                throw;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }
}
