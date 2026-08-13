using System.Security.Cryptography;
using System.Text;
using EMF.Security.Encryption;
using EMF.Security.Encryption.Models;
using EMF.Security.Encryption.Services;

namespace EMF.Tests;

public sealed class DevelopmentEncryptionServiceTests
{
    private static (
        DevelopmentEncryptionService Service,
        EncryptionKey Key)
        CreateService()
    {
        var key =
            new EncryptionKey
            {
                KeyId = "development-key-001",
                KeyMaterial =
                    RandomNumberGenerator.GetBytes(32)
            };

        var provider =
            new InMemoryEncryptionKeyProvider(
                new[] { key });

        return (
            new DevelopmentEncryptionService(provider),
            key);
    }

    [Fact]
    public async Task EncryptThenDecrypt_ReturnsOriginalPlaintext()
    {
        var (service, _) = CreateService();

        var plaintext =
            Encoding.UTF8.GetBytes(
                "Protected EMF evidence.");

        var encrypted =
            await service.EncryptAsync(plaintext);

        var decrypted =
            await service.DecryptAsync(encrypted);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public async Task TamperedCiphertext_IsRejected()
    {
        var (service, _) = CreateService();

        var plaintext =
            Encoding.UTF8.GetBytes(
                "Protected EMF evidence.");

        var encrypted =
            await service.EncryptAsync(plaintext);

        encrypted.Ciphertext[0] ^= 0x01;

        await Assert.ThrowsAsync<AuthenticationTagMismatchException>(
            () => service.DecryptAsync(encrypted));
    }

    [Fact]
    public async Task WrongKeyId_IsRejected()
    {
        var (service, _) = CreateService();

        var plaintext =
            Encoding.UTF8.GetBytes(
                "Protected EMF evidence.");

        var encrypted =
            await service.EncryptAsync(plaintext);

        var wrongKeyContent =
            new EncryptedContent
            {
                Ciphertext = encrypted.Ciphertext,
                Nonce = encrypted.Nonce,
                AuthenticationTag = encrypted.AuthenticationTag,
                KeyId = "wrong-key"
            };

        await Assert.ThrowsAsync<CryptographicException>(
            () => service.DecryptAsync(wrongKeyContent));
    }
}
