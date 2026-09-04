using System.Security.Cryptography;
using System.Text;
using EMF.Security.Azure.Cryptography;
using EMF.Security.Azure.Encryption;
using EMF.Security.Azure.Keys;
using EMF.Security.Encryption.Envelope;

namespace EMF.Tests;

public sealed class AzureEnvelopeEncryptionServiceTests
{
    [Fact]
    public async Task EncryptThenDecrypt_RoundTripsPlaintext()
    {
        var keyReference =
            new AzureKeyReference
            {
                KeyName = "emf-key",
                KeyVersion = "v1"
            };

        var cryptography = new FakeCryptography();

        var service =
            new AzureEnvelopeEncryptionService(
                new FakeKeyProvider(keyReference),
                new FakeFactory(cryptography));

        var plaintext = Encoding.UTF8.GetBytes("hello emf");

        var envelope =
            await service.EncryptAsync(plaintext);

        var result =
            await service.DecryptAsync(envelope);

        Assert.Equal(plaintext, result);
        Assert.Equal(
            EncryptedEnvelopeFormat.CurrentVersion,
            envelope.FormatVersion);
        Assert.Equal(
            EncryptedEnvelopeFormat.Aes256GcmAlgorithm,
            envelope.Algorithm);
    }


    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(31)]
    [InlineData(33)]
    public async Task DecryptAsync_RejectsNon256BitDataEncryptionKey(
        int keyLength)
    {
        var keyReference =
            new AzureKeyReference
            {
                KeyName = "emf-key",
                KeyVersion = "v1"
            };

        var cryptography =
            new FakeCryptography(
                new byte[keyLength]);

        var service =
            new AzureEnvelopeEncryptionService(
                new FakeKeyProvider(keyReference),
                new FakeFactory(cryptography));

        var envelope =
            await service.EncryptAsync(
                Encoding.UTF8.GetBytes("protected"));

        await Assert.ThrowsAsync<CryptographicException>(
            () => service.DecryptAsync(envelope));
    }


    [Fact]
    public async Task DecryptAsync_RejectsWrongReturnedKeyIdentity()
    {
        var cryptography =
            new FakeCryptography();

        var encryptingService =
            new AzureEnvelopeEncryptionService(
                new FakeKeyProvider(
                    new AzureKeyReference
                    {
                        KeyName = "emf-key",
                        KeyVersion = "v1"
                    }),
                new FakeFactory(cryptography));

        var envelope =
            await encryptingService.EncryptAsync(
                Encoding.UTF8.GetBytes("protected"));

        var decryptingService =
            new AzureEnvelopeEncryptionService(
                new FakeKeyProvider(
                    new AzureKeyReference
                    {
                        KeyName = "emf-key",
                        KeyVersion = "v2"
                    }),
                new FakeFactory(cryptography));

        await Assert.ThrowsAsync<CryptographicException>(
            () => decryptingService.DecryptAsync(envelope));
    }

    [Fact]
    public async Task DecryptWithContextAsync_RejectsWrongContext()
    {
        var keyReference = new AzureKeyReference
        {
            KeyName = "emf-key",
            KeyVersion = "v1"
        };

        var service =
            new AzureEnvelopeEncryptionService(
                new FakeKeyProvider(keyReference),
                new FakeFactory(new FakeCryptography()));

        var envelope =
            await service.EncryptWithContextAsync(
                Encoding.UTF8.GetBytes("protected"),
                Encoding.UTF8.GetBytes("artifact-a"));

        Assert.Equal(
            EncryptedEnvelopeFormat.ContextBoundVersion,
            envelope.FormatVersion);

        await Assert.ThrowsAnyAsync<CryptographicException>(
            () => service.DecryptWithContextAsync(
                envelope,
                Encoding.UTF8.GetBytes("artifact-b")));
    }

    private sealed class FakeKeyProvider :
        IAzureKeyReferenceProvider
    {
        private readonly AzureKeyReference _reference;

        public FakeKeyProvider(AzureKeyReference reference)
        {
            _reference = reference;
        }

        public Task<AzureKeyReference> GetCurrentKeyAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_reference);

        public Task<AzureKeyReference?> GetKeyAsync(
            string keyIdentifier,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AzureKeyReference?>(_reference);
    }

    private sealed class FakeFactory :
        IAzureKeyCryptographyFactory
    {
        private readonly IAzureKeyCryptography _cryptography;

        public FakeFactory(IAzureKeyCryptography cryptography)
        {
            _cryptography = cryptography;
        }

        public IAzureKeyCryptography Create(
            AzureKeyReference keyReference) =>
            _cryptography;
    }

    private sealed class FakeCryptography :
        IAzureKeyCryptography
    {
        private readonly byte[]? _unwrappedKey;

        public FakeCryptography(
            byte[]? unwrappedKey = null)
        {
            _unwrappedKey = unwrappedKey;
        }

        public Task<byte[]> WrapKeyAsync(
            byte[] key,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(key.ToArray());

        public Task<byte[]> UnwrapKeyAsync(
            byte[] wrappedKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                (_unwrappedKey ?? wrappedKey).ToArray());
    }
}
