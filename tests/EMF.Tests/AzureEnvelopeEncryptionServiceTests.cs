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
        public Task<byte[]> WrapKeyAsync(
            byte[] key,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(key.ToArray());

        public Task<byte[]> UnwrapKeyAsync(
            byte[] wrappedKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(wrappedKey.ToArray());
    }
}
