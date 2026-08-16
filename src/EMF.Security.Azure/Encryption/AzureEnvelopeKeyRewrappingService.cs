using System.Security.Cryptography;
using EMF.Security.Azure.Cryptography;
using EMF.Security.Azure.Keys;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Models;

namespace EMF.Security.Azure.Encryption;

public sealed class AzureEnvelopeKeyRewrappingService :
    IEnvelopeKeyRewrappingService
{
    private readonly IAzureKeyReferenceProvider _keyProvider;
    private readonly IAzureKeyCryptographyFactory
        _cryptographyFactory;

    public AzureEnvelopeKeyRewrappingService(
        IAzureKeyReferenceProvider keyProvider,
        IAzureKeyCryptographyFactory cryptographyFactory)
    {
        ArgumentNullException.ThrowIfNull(keyProvider);
        ArgumentNullException.ThrowIfNull(
            cryptographyFactory);

        _keyProvider = keyProvider;
        _cryptographyFactory = cryptographyFactory;
    }

    public async Task<EncryptedEnvelope> RewrapAsync(
        EncryptedEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();

        var currentKey =
            await _keyProvider.GetCurrentKeyAsync(
                cancellationToken);

        var currentKeyId =
            GetKeyIdentifier(currentKey);

        if (string.Equals(
            envelope.KeyEncryptionKeyId,
            currentKeyId,
            StringComparison.Ordinal))
        {
            return envelope;
        }

        var historicalKey =
            await _keyProvider.GetKeyAsync(
                envelope.KeyEncryptionKeyId,
                cancellationToken);

        if (historicalKey is null)
        {
            throw new CryptographicException(
                "Historical encryption key not found.");
        }

        var historicalCryptography =
            _cryptographyFactory.Create(
                historicalKey);

        var currentCryptography =
            _cryptographyFactory.Create(
                currentKey);

        var dataEncryptionKey =
            await historicalCryptography.UnwrapKeyAsync(
                envelope.WrappedDataEncryptionKey,
                cancellationToken);

        try
        {
            var wrappedDataEncryptionKey =
                await currentCryptography.WrapKeyAsync(
                    dataEncryptionKey,
                    cancellationToken);

            var verificationKey =
                await currentCryptography.UnwrapKeyAsync(
                    wrappedDataEncryptionKey,
                    cancellationToken);

            try
            {
                if (!CryptographicOperations.FixedTimeEquals(
                    verificationKey,
                    dataEncryptionKey))
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
            string.IsNullOrWhiteSpace(
                keyReference.KeyVersion))
        {
            throw new CryptographicException(
                "Key name and version are required.");
        }

        return
            $"{keyReference.KeyName}/" +
            $"{keyReference.KeyVersion}";
    }
}
