using System.Security.Cryptography;
using System.Text;
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
            "AES-256-GCM",
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
}
