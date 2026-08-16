using System.Security.Cryptography;
using System.Text;
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
}
