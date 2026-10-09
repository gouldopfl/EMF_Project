using System.Security.Cryptography;
using EMF.Security.Azure.Cryptography;
using EMF.Security.Azure.Keys;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Models;

namespace EMF.Security.Azure.Encryption;

public sealed class AzureEnvelopeEncryptionService :
    IEnvelopeEncryptionService, IBoundedEnvelopeDecryptionService
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
            await AzureEnvelopeProviderBoundary.CallAsync(() => _keyProvider.GetCurrentKeyAsync(cancellationToken), EnvelopeKeyOperation.ResolveCurrentKey);

        if (string.IsNullOrWhiteSpace(keyReference.KeyVersion))
            throw new CryptographicException("Key version is required.");

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

        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        var dek = RandomNumberGenerator.GetBytes(32);

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
                await AzureEnvelopeProviderBoundary.CallAsync(() => Task.FromResult(_cryptographyFactory.Create(keyReference)), EnvelopeKeyOperation.UnwrapKey);

            if (cryptography is null)
                throw new CryptographicException(
                    "Encryption key cryptography factory returned no implementation.");

            var wrappedDek =
                await AzureEnvelopeProviderBoundary.CallAsync(() => cryptography.WrapKeyAsync(dek, cancellationToken), EnvelopeKeyOperation.WrapKey);

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

    public Task<byte[]> DecryptWithContextBoundedAsync(EncryptedEnvelope envelope,
        ReadOnlyMemory<byte> authenticatedContext, EnvelopeDecryptionLimits limits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(limits);
        EnvelopeContentAuthentication.ValidateBound(envelope, limits.MaximumPlaintextBytes);
        if (authenticatedContext.Length > limits.MaximumAuthenticatedContextBytes || envelope.FormatVersion != 2)
            throw new CryptographicException("Bounded context-bound envelope required.");
        return DecryptCoreAsync(envelope, authenticatedContext, cancellationToken, limits);
    }

    private async Task<byte[]> DecryptCoreAsync(
        EncryptedEnvelope envelope,
        ReadOnlyMemory<byte>? authenticatedContext,
        CancellationToken cancellationToken, EnvelopeDecryptionLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();
        EncryptedEnvelopeFormat.Validate(envelope);


        var parts = envelope.KeyEncryptionKeyId.Split('/', 2);

        if (parts.Length != 2)
            throw new CryptographicException("Invalid key identifier.");

        var keyReference = await AzureEnvelopeProviderBoundary.CallAsync(() => _keyProvider.GetKeyAsync(envelope.KeyEncryptionKeyId, cancellationToken), EnvelopeKeyOperation.ResolveHistoricalKey);

        if (keyReference is null)
            throw new CryptographicException("Encryption key not found.");

        if (!string.Equals(
                keyReference.KeyName,
                parts[0],
                StringComparison.Ordinal) ||
            !string.Equals(
                keyReference.KeyVersion,
                parts[1],
                StringComparison.Ordinal))
        {
            throw new CryptographicException(
                "Encryption key identity mismatch.");
        }

        var cryptography =
            await AzureEnvelopeProviderBoundary.CallAsync(() => Task.FromResult(_cryptographyFactory.Create(keyReference)), EnvelopeKeyOperation.UnwrapKey);

        if (cryptography is null)
            throw new CryptographicException(
                "Encryption key cryptography factory returned no implementation.");

        var dek = await AzureEnvelopeProviderBoundary.CallAsync(() => cryptography.UnwrapKeyAsync(envelope.WrappedDataEncryptionKey, cancellationToken), EnvelopeKeyOperation.UnwrapKey);

        try
        {
            if (dek.Length != 32)
            {
                throw new CryptographicException(
                    "Invalid data encryption key length.");
            }

            return limits is null
                ? EnvelopeContentAuthentication.Decrypt(envelope, dek, authenticatedContext)
                : EnvelopeContentAuthentication.DecryptBounded(envelope, dek, authenticatedContext!.Value, limits, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }
}
