using System.Security.Cryptography;
using System.Text;
using EMF.Security.Encryption.Services;

namespace EMF.Tests;

public sealed class DevelopmentEncryptionServiceTests
{
    [Fact]
    public async Task EncryptThenDecrypt_ReturnsOriginalPlaintext()
    {
        var service =
            new DevelopmentEncryptionService(
                RandomNumberGenerator.GetBytes(32),
                "development-key-001");

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
        var service =
            new DevelopmentEncryptionService(
                RandomNumberGenerator.GetBytes(32),
                "development-key-001");

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
        var service =
            new DevelopmentEncryptionService(
                RandomNumberGenerator.GetBytes(32),
                "development-key-001");

        var plaintext =
            Encoding.UTF8.GetBytes(
                "Protected EMF evidence.");

        var encrypted =
            await service.EncryptAsync(plaintext);

        var wrongKeyContent =
            new EMF.Security.Encryption.EncryptedContent
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
