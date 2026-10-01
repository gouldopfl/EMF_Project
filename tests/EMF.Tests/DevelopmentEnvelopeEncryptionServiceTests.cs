using System.Security.Cryptography;
using System.Text;
using EMF.Security.Encryption;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Models;
using EMF.Security.Encryption.Envelope.Services;
using EMF.Security.Encryption.Models;
using EMF.Security.Encryption.Services;

namespace EMF.Tests;

public sealed class DevelopmentEnvelopeEncryptionServiceTests
{
    private static DevelopmentEnvelopeEncryptionService CreateService()
    {
        var key =
            new EncryptionKey
            {
                KeyId = "kek-001",
                KeyMaterial = RandomNumberGenerator.GetBytes(32)
            };

        var keyProvider =
            new InMemoryEncryptionKeyProvider(
                new[] { key });

        return new DevelopmentEnvelopeEncryptionService(
            keyProvider);
    }

    [Fact]
    public async Task EncryptThenDecrypt_ReturnsOriginalPlaintext()
    {
        var service = CreateService();

        var plaintext =
            Encoding.UTF8.GetBytes(
                "Protected EMF evidence.");

        var encrypted =
            await service.EncryptAsync(plaintext);

        var decrypted =
            await service.DecryptAsync(encrypted);

        Assert.Equal(plaintext, decrypted);
        Assert.Equal(
            "kek-001",
            encrypted.KeyEncryptionKeyId);
        Assert.Equal(
            EncryptedEnvelopeFormat.CurrentVersion,
            encrypted.FormatVersion);
        Assert.Equal(
            EncryptedEnvelopeFormat.Aes256GcmAlgorithm,
            encrypted.Algorithm);
    }

    [Fact]
    public async Task EncryptedEnvelope_ContainsWrappedDataEncryptionKey()
    {
        var service = CreateService();

        var plaintext =
            Encoding.UTF8.GetBytes(
                "Protected EMF evidence.");

        var encrypted =
            await service.EncryptAsync(plaintext);

        Assert.NotEmpty(
            encrypted.WrappedDataEncryptionKey);

        Assert.NotEmpty(encrypted.Nonce);
        Assert.NotEmpty(encrypted.AuthenticationTag);
    }

    [Fact]
    public async Task DecryptAsync_TamperedAlgorithmFails()
    {
        var service = CreateService();
        var plaintext =
            Encoding.UTF8.GetBytes("Protected evidence.");

        var encrypted =
            await service.EncryptAsync(plaintext);

        var tampered =
            new EncryptedEnvelope
            {
                FormatVersion = encrypted.FormatVersion,
                Ciphertext = encrypted.Ciphertext,
                Nonce = encrypted.Nonce,
                AuthenticationTag = encrypted.AuthenticationTag,
                WrappedDataEncryptionKey =
                    encrypted.WrappedDataEncryptionKey,
                KeyEncryptionKeyId =
                    encrypted.KeyEncryptionKeyId,
                Algorithm = "AES-128-GCM"
            };

        await Assert.ThrowsAsync<CryptographicException>(
            () => service.DecryptAsync(tampered));
    }

    [Fact]
    public async Task DecryptAsync_DowngradedFormatFails()
    {
        var service = CreateService();
        var plaintext =
            Encoding.UTF8.GetBytes("Protected evidence.");

        var encrypted =
            await service.EncryptAsync(plaintext);

        var tampered =
            new EncryptedEnvelope
            {
                FormatVersion =
                    EncryptedEnvelopeFormat.LegacyVersion,
                Ciphertext = encrypted.Ciphertext,
                Nonce = encrypted.Nonce,
                AuthenticationTag = encrypted.AuthenticationTag,
                WrappedDataEncryptionKey =
                    encrypted.WrappedDataEncryptionKey,
                KeyEncryptionKeyId =
                    encrypted.KeyEncryptionKeyId,
                Algorithm = encrypted.Algorithm
            };

        await Assert.ThrowsAsync<AuthenticationTagMismatchException>(
            () => service.DecryptAsync(tampered));
    }

    [Fact]
    public async Task Operations_RejectMismatchedReturnedKeyId()
    {
        var validService = CreateService();
        var envelope =
            await validService.EncryptAsync(
                Encoding.UTF8.GetBytes("protected"));

        var mismatchedService =
            new DevelopmentEnvelopeEncryptionService(
                new MismatchedKeyProvider());

        await Assert.ThrowsAsync<CryptographicException>(
            () => mismatchedService.EncryptAsync(
                Encoding.UTF8.GetBytes("protected")));

        await Assert.ThrowsAsync<CryptographicException>(
            () => mismatchedService.DecryptAsync(
                envelope));
    }

    [Theory]
    [InlineData("ciphertext")]
    [InlineData("nonce")]
    [InlineData("tag")]
    [InlineData("wrapped-dek")]
    public async Task DecryptAsync_RejectsAuthenticatedEnvelopeTampering(string field)
    {
        var service = CreateService();
        var envelope = await service.EncryptAsync(new byte[] { 1, 2, 3 });
        var bytes = field switch
        {
            "ciphertext" => envelope.Ciphertext,
            "nonce" => envelope.Nonce,
            "tag" => envelope.AuthenticationTag,
            _ => envelope.WrappedDataEncryptionKey
        };
        bytes[0] ^= 1;

        await Assert.ThrowsAnyAsync<CryptographicException>(() => service.DecryptAsync(envelope));
    }

    [Fact]
    public async Task DecryptAsync_RejectsWrongKeyMaterialWithSameIdentity()
    {
        var envelope = await CreateService().EncryptAsync(new byte[] { 1, 2, 3 });
        var wrongKey = new EncryptionKey
        {
            KeyId = envelope.KeyEncryptionKeyId,
            KeyMaterial = RandomNumberGenerator.GetBytes(32)
        };
        var service = new DevelopmentEnvelopeEncryptionService(
            new InMemoryEncryptionKeyProvider([wrongKey]));
        try
        {
            await Assert.ThrowsAnyAsync<CryptographicException>(() => service.DecryptAsync(envelope));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrongKey.KeyMaterial);
        }
    }

    private sealed class MismatchedKeyProvider :
        IEncryptionKeyProvider
    {
        private readonly EncryptionKey _key =
            new()
            {
                KeyId = "returned-key",
                KeyMaterial = new byte[32]
            };

        public Task<string?> GetCurrentKeyIdAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<string?>("kek-001");
        }

        public Task<EncryptionKey?> GetKeyAsync(
            string keyId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<EncryptionKey?>(_key);
        }
    }
}
