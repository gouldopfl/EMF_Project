using EMF.Security.Azure.Cryptography;
using EMF.Security.Azure.Encryption;
using EMF.Security.Azure.Keys;
using EMF.Security.Encryption.Envelope.Models;

namespace EMF.Tests;

public sealed class
    AzureEnvelopeKeyRewrappingServiceTests
{
    [Fact]
    public async Task RewrapAsync_UsesCurrentAzureKey()
    {
        var historicalKey = CreateKey("v1");
        var currentKey = CreateKey("v2");

        var service =
            new AzureEnvelopeKeyRewrappingService(
                new TestKeyProvider(
                    historicalKey,
                    currentKey),
                new TestCryptographyFactory());

        var original =
            new EncryptedEnvelope
            {
                Ciphertext = [1, 2, 3],
                Nonce = [4, 5, 6],
                AuthenticationTag = [7, 8, 9],
                WrappedDataEncryptionKey = [1, 42, 43],
                KeyEncryptionKeyId = "emf-key/v1",
                Algorithm = "AES-256-GCM"
            };

        var rewrapped =
            await service.RewrapAsync(original);

        Assert.Equal(
            "emf-key/v2",
            rewrapped.KeyEncryptionKeyId);

        Assert.Equal(
            original.Ciphertext,
            rewrapped.Ciphertext);

        Assert.Equal(
            original.Nonce,
            rewrapped.Nonce);

        Assert.Equal(
            original.AuthenticationTag,
            rewrapped.AuthenticationTag);

        Assert.Equal([2, 42, 43],
            rewrapped.WrappedDataEncryptionKey);

        Assert.Same(
            rewrapped,
            await service.RewrapAsync(rewrapped));
    }

    private static AzureKeyReference CreateKey(
        string version)
    {
        return new AzureKeyReference
        {
            KeyName = "emf-key",
            KeyVersion = version
        };
    }

    private sealed class TestKeyProvider :
        IAzureKeyReferenceProvider
    {
        private readonly AzureKeyReference _historical;
        private readonly AzureKeyReference _current;

        public TestKeyProvider(
            AzureKeyReference historical,
            AzureKeyReference current)
        {
            _historical = historical;
            _current = current;
        }

        public Task<AzureKeyReference> GetCurrentKeyAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_current);
        }

        public Task<AzureKeyReference?> GetKeyAsync(
            string keyIdentifier,
            CancellationToken cancellationToken = default)
        {
            AzureKeyReference? result =
                keyIdentifier == "emf-key/v1"
                    ? _historical
                    : null;

            return Task.FromResult(result);
        }
    }

    private sealed class TestCryptographyFactory :
        IAzureKeyCryptographyFactory
    {
        public IAzureKeyCryptography Create(
            AzureKeyReference keyReference)
        {
            var marker =
                keyReference.KeyVersion == "v1"
                    ? (byte)1
                    : (byte)2;

            return new MarkerCryptography(marker);
        }
    }

    private sealed class MarkerCryptography :
        IAzureKeyCryptography
    {
        private readonly byte _marker;

        public MarkerCryptography(byte marker)
        {
            _marker = marker;
        }

        public Task<byte[]> WrapKeyAsync(
            byte[] key,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new[] { _marker }
                    .Concat(key)
                    .ToArray());
        }

        public Task<byte[]> UnwrapKeyAsync(
            byte[] wrappedKey,
            CancellationToken cancellationToken = default)
        {
            if (wrappedKey.Length == 0 ||
                wrappedKey[0] != _marker)
            {
                throw new InvalidOperationException(
                    "Unexpected key marker.");
            }

            return Task.FromResult(
                wrappedKey[1..]);
        }
    }
}
