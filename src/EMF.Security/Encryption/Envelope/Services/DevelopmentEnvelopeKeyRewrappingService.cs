using System.Security.Cryptography;
using EMF.Security.Encryption.Envelope.Models;
using EMF.Security.Encryption.Models;

namespace EMF.Security.Encryption.Envelope.Services;

public sealed class
    DevelopmentEnvelopeKeyRewrappingService :
    IEnvelopeKeyRewrappingService, IAuthenticatedEnvelopeKeyRewrappingService
{
    private const int KeySize = 32;

    private readonly IEncryptionKeyProvider _keyProvider;
    private readonly int _maximumBytes;

    public DevelopmentEnvelopeKeyRewrappingService(
        IEncryptionKeyProvider keyProvider, int maximumPlaintextBytes = EnvelopeContentAuthentication.DefaultMaximumPlaintextBytes)
    {
        ArgumentNullException.ThrowIfNull(keyProvider);

        if (maximumPlaintextBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumPlaintextBytes));
        _maximumBytes = maximumPlaintextBytes;
        _keyProvider = keyProvider;
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
            await EnvelopeProviderBoundary.CallAsync(() => _keyProvider.GetKeyAsync(envelope.KeyEncryptionKeyId, cancellationToken), EnvelopeKeyOperation.ResolveHistoricalKey, "development");

        ValidateKey(historicalKey, envelope.KeyEncryptionKeyId);
        EnvelopeContentAuthentication.ValidateBound(envelope, _maximumBytes);
        var dataEncryptionKey =
            DevelopmentDataEncryptionKeyWrapper.Unwrap(
                historicalKey!.KeyMaterial,
                envelope.WrappedDataEncryptionKey);

        try
        {
            EnvelopeContentAuthentication.Authenticate(envelope, dataEncryptionKey, context, _maximumBytes);
            var currentKeyId =
                await EnvelopeProviderBoundary.CallAsync(() => _keyProvider.GetCurrentKeyIdAsync(cancellationToken), EnvelopeKeyOperation.ResolveCurrentKey, "development");

            if (string.IsNullOrWhiteSpace(currentKeyId))
            {
                throw new CryptographicException(
                    "No current key.");
            }

            if (envelope.KeyEncryptionKeyId == currentKeyId)
            {
                return envelope;
            }

            var currentKey = await EnvelopeProviderBoundary.CallAsync(() => _keyProvider.GetKeyAsync(currentKeyId, cancellationToken), EnvelopeKeyOperation.ResolveCurrentKey, "development");
            ValidateKey(currentKey, currentKeyId);

            var wrappedDataEncryptionKey =
                DevelopmentDataEncryptionKeyWrapper.Wrap(
                    currentKey!.KeyMaterial,
                    dataEncryptionKey);

            Verify(
                currentKey.KeyMaterial,
                wrappedDataEncryptionKey,
                dataEncryptionKey);

            return new EncryptedEnvelope
            {
                FormatVersion =
                    envelope.FormatVersion,
                Ciphertext = envelope.Ciphertext.ToArray(),
                Nonce = envelope.Nonce.ToArray(),
                AuthenticationTag =
                    envelope.AuthenticationTag.ToArray(),
                WrappedDataEncryptionKey =
                    wrappedDataEncryptionKey,
                KeyEncryptionKeyId = currentKey.KeyId,
                Algorithm = envelope.Algorithm
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(
                dataEncryptionKey);
        }
    }

    private static void ValidateKey(
        EncryptionKey? key,
        string expectedKeyId)
    {
        if (key is null) throw new EnvelopeProviderFailure(EnvelopeKeyOperation.ResolveHistoricalKey, EnvelopeFailureCategory.KeyUnavailable, "development", EnvelopeFailureRetryability.No);
        if (
            key.KeyMaterial is not { Length: KeySize } ||
            !string.Equals(
                key.KeyId,
                expectedKeyId,
                StringComparison.Ordinal))
        {
            throw new EnvelopeProviderFailure(EnvelopeKeyOperation.ResolveHistoricalKey, EnvelopeFailureCategory.KeyVerificationFailed, "development", EnvelopeFailureRetryability.No);
        }
    }

    private static void Verify(
        byte[] currentKey,
        byte[] wrappedDataEncryptionKey,
        byte[] expectedDataEncryptionKey)
    {
        var verificationKey =
            DevelopmentDataEncryptionKeyWrapper.Unwrap(
                currentKey,
                wrappedDataEncryptionKey);

        try
        {
            if (!CryptographicOperations.FixedTimeEquals(
                verificationKey,
                expectedDataEncryptionKey))
            {
                throw new EnvelopeProviderFailure(EnvelopeKeyOperation.UnwrapKey, EnvelopeFailureCategory.KeyVerificationFailed, "development", EnvelopeFailureRetryability.No);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(
                verificationKey);
        }
    }
}
