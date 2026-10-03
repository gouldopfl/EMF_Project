using System.Security.Cryptography;
using EMF.Security.Azure.Cryptography;
using EMF.Security.Azure.Keys;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Models;

namespace EMF.Security.Azure.Encryption;

public sealed class AzureEnvelopeKeyRewrappingService :
    IEnvelopeKeyRewrappingService, IAuthenticatedEnvelopeKeyRewrappingService
{
    private const int DataEncryptionKeySize = 32;
    private readonly int _maximumBytes;
    private readonly IAzureKeyReferenceProvider _keyProvider;
    private readonly IAzureKeyCryptographyFactory
        _cryptographyFactory;

    public AzureEnvelopeKeyRewrappingService(
        IAzureKeyReferenceProvider keyProvider,
        IAzureKeyCryptographyFactory cryptographyFactory,
        int maximumPlaintextBytes = EnvelopeContentAuthentication.DefaultMaximumPlaintextBytes)
    {
        ArgumentNullException.ThrowIfNull(keyProvider);
        ArgumentNullException.ThrowIfNull(
            cryptographyFactory);

        if (maximumPlaintextBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumPlaintextBytes));
        _maximumBytes = maximumPlaintextBytes;
        _keyProvider = keyProvider;
        _cryptographyFactory = cryptographyFactory;
    }

    public Task<EncryptedEnvelope> RewrapAsync(EncryptedEnvelope envelope, CancellationToken cancellationToken = default)
        => RewrapCoreAsync(envelope, null, cancellationToken);
    public Task<EncryptedEnvelope> RewrapAuthenticatedAsync(EncryptedEnvelope envelope,
        ReadOnlyMemory<byte> authenticatedContext, CancellationToken cancellationToken = default)
    {
        if (envelope.FormatVersion != EncryptedEnvelopeFormat.ContextBoundVersion)
            throw new CryptographicException("Artifact content requires an identity-bound envelope.");
        return RewrapCoreAsync(envelope, authenticatedContext, cancellationToken);
    }
    private async Task<EncryptedEnvelope> RewrapCoreAsync(EncryptedEnvelope envelope,
        ReadOnlyMemory<byte>? context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();
        EnvelopeContentAuthentication.ValidateBound(envelope, _maximumBytes);

        var historicalKey =
            await AzureEnvelopeProviderBoundary.CallAsync(() => _keyProvider.GetKeyAsync(envelope.KeyEncryptionKeyId, cancellationToken), EnvelopeKeyOperation.ResolveHistoricalKey);

        if (historicalKey is null)
        {
            throw new EnvelopeProviderFailure(EnvelopeKeyOperation.ResolveHistoricalKey, EnvelopeFailureCategory.KeyUnavailable, "azure", EnvelopeFailureRetryability.No);
        }

        if (!string.Equals(
                GetKeyIdentifier(historicalKey),
                envelope.KeyEncryptionKeyId,
                StringComparison.Ordinal))
        {
            throw new EnvelopeProviderFailure(EnvelopeKeyOperation.ResolveHistoricalKey, EnvelopeFailureCategory.KeyVerificationFailed, "azure", EnvelopeFailureRetryability.No);
        }

        var historicalCryptography =
            await AzureEnvelopeProviderBoundary.CallAsync(() => Task.FromResult(_cryptographyFactory.Create(historicalKey)), EnvelopeKeyOperation.UnwrapKey);

        EnvelopeContentAuthentication.ValidateBound(envelope, _maximumBytes);
        var dataEncryptionKey =
            await AzureEnvelopeProviderBoundary.CallAsync(() => historicalCryptography.UnwrapKeyAsync(envelope.WrappedDataEncryptionKey, cancellationToken), EnvelopeKeyOperation.UnwrapKey);

        try
        {
            if (dataEncryptionKey.Length !=
                DataEncryptionKeySize)
            {
                throw new CryptographicException(
                    "Invalid data encryption key length.");
            }

            EnvelopeContentAuthentication.Authenticate(envelope, dataEncryptionKey, context, _maximumBytes, "azure");
            var currentKey =
                await AzureEnvelopeProviderBoundary.CallAsync(() => _keyProvider.GetCurrentKeyAsync(cancellationToken), EnvelopeKeyOperation.ResolveCurrentKey);

            var currentKeyId =
                GetKeyIdentifier(currentKey);

            if (string.Equals(
                envelope.KeyEncryptionKeyId,
                currentKeyId,
                StringComparison.Ordinal))
            {
                return envelope;
            }

            var currentCryptography = await AzureEnvelopeProviderBoundary.CallAsync(() => Task.FromResult(_cryptographyFactory.Create(currentKey)), EnvelopeKeyOperation.UnwrapKey);
            var wrappedDataEncryptionKey =
                await AzureEnvelopeProviderBoundary.CallAsync(() => currentCryptography.WrapKeyAsync(dataEncryptionKey, cancellationToken), EnvelopeKeyOperation.WrapKey);

            var verificationKey =
                await AzureEnvelopeProviderBoundary.CallAsync(() => currentCryptography.UnwrapKeyAsync(wrappedDataEncryptionKey, cancellationToken), EnvelopeKeyOperation.UnwrapKey);

            try
            {
                if (!CryptographicOperations.FixedTimeEquals(
                    verificationKey,
                    dataEncryptionKey))
                {
                    throw new EnvelopeProviderFailure(EnvelopeKeyOperation.UnwrapKey, EnvelopeFailureCategory.KeyVerificationFailed, "azure", EnvelopeFailureRetryability.No);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(
                    verificationKey);
            }

            return new EncryptedEnvelope
            {
                FormatVersion =
                    envelope.FormatVersion,
                Ciphertext =
                    envelope.Ciphertext.ToArray(),
                Nonce =
                    envelope.Nonce.ToArray(),
                AuthenticationTag =
                    envelope.AuthenticationTag.ToArray(),
                WrappedDataEncryptionKey =
                    wrappedDataEncryptionKey,
                KeyEncryptionKeyId =
                    currentKeyId,
                Algorithm =
                    envelope.Algorithm
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(
                dataEncryptionKey);
        }
    }

    private static string GetKeyIdentifier(
        AzureKeyReference keyReference)
    {
        ArgumentNullException.ThrowIfNull(keyReference);

        if (string.IsNullOrWhiteSpace(
                keyReference.KeyName) ||
            string.IsNullOrWhiteSpace(keyReference.KeyVersion) || keyReference.KeyName.Length > 128 || keyReference.KeyVersion.Length > 128 ||
            keyReference.KeyName.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')) ||
            keyReference.KeyVersion.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')))
        {
            throw new CryptographicException(
                "Key name and version are required.");
        }

        return
            $"{keyReference.KeyName}/" +
            $"{keyReference.KeyVersion}";
    }
}
