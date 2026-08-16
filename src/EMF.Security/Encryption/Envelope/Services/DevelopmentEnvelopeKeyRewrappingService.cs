using System.Security.Cryptography;
using EMF.Security.Encryption.Envelope.Models;
using EMF.Security.Encryption.Models;

namespace EMF.Security.Encryption.Envelope.Services;

public sealed class
    DevelopmentEnvelopeKeyRewrappingService :
    IEnvelopeKeyRewrappingService
{
    private const int KeySize = 32;

    private readonly IEncryptionKeyProvider _keyProvider;

    public DevelopmentEnvelopeKeyRewrappingService(
        IEncryptionKeyProvider keyProvider)
    {
        ArgumentNullException.ThrowIfNull(keyProvider);

        _keyProvider = keyProvider;
    }

    public async Task<EncryptedEnvelope> RewrapAsync(
        EncryptedEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();

        var currentKeyId =
            await _keyProvider.GetCurrentKeyIdAsync(
                cancellationToken);

        if (string.IsNullOrWhiteSpace(currentKeyId))
        {
            throw new CryptographicException(
                "No current key.");
        }

        if (envelope.KeyEncryptionKeyId == currentKeyId)
        {
            return envelope;
        }

        var historicalKey =
            await _keyProvider.GetKeyAsync(
                envelope.KeyEncryptionKeyId,
                cancellationToken);

        var currentKey =
            await _keyProvider.GetKeyAsync(
                currentKeyId,
                cancellationToken);

        ValidateKey(historicalKey);
        ValidateKey(currentKey);

        var dataEncryptionKey =
            DevelopmentDataEncryptionKeyWrapper.Unwrap(
                historicalKey!.KeyMaterial,
                envelope.WrappedDataEncryptionKey);

        try
        {
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

    private static void ValidateKey(EncryptionKey? key)
    {
        if (key is null ||
            string.IsNullOrWhiteSpace(key.KeyId) ||
            key.KeyMaterial.Length != KeySize)
        {
            throw new CryptographicException(
                "Invalid encryption key.");
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
                throw new CryptographicException(
                    "Rewrapped key verification failed.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(
                verificationKey);
        }
    }
}
