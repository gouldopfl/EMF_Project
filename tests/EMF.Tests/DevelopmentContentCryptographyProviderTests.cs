using System.Security.Cryptography;
using System.Text;
using EMF.Security.Encryption;
using EMF.Security.Encryption.Models;
using EMF.Security.Encryption.Providers.Models;
using EMF.Security.Encryption.Providers.Services;
using EMF.Security.Encryption.Services;

namespace EMF.Tests;

public sealed class DevelopmentContentCryptographyProviderTests
{
    [Fact]
    public async Task EncryptThenDecrypt_ReturnsOriginalPlaintext()
    {
        var key =
            new EncryptionKey
            {
                KeyId = "key-001",
                KeyMaterial =
                    RandomNumberGenerator.GetBytes(32)
            };

        var keyProvider =
            new InMemoryEncryptionKeyProvider(
                new[] { key });

        var provider =
            new DevelopmentContentCryptographyProvider(
                keyProvider);

        var plaintext =
            Encoding.UTF8.GetBytes(
                "Protected EMF evidence.");

        var encrypted =
            await provider.EncryptAsync(plaintext);

        var decrypted =
            await provider.DecryptAsync(
                new ContentDecryptionRequest
                {
                    Ciphertext = encrypted.Ciphertext,
                    Nonce = encrypted.Nonce,
                    AuthenticationTag = encrypted.AuthenticationTag,
                    KeyId = encrypted.KeyId
                });

        Assert.Equal("key-001", encrypted.KeyId);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public async Task TamperedCiphertext_IsRejected()
    {
        var key =
            new EncryptionKey
            {
                KeyId = "key-001",
                KeyMaterial =
                    RandomNumberGenerator.GetBytes(32)
            };

        var keyProvider =
            new InMemoryEncryptionKeyProvider(
                new[] { key });

        var provider =
            new DevelopmentContentCryptographyProvider(
                keyProvider);

        var encrypted =
            await provider.EncryptAsync(
                Encoding.UTF8.GetBytes(
                    "Protected EMF evidence."));

        encrypted.Ciphertext[0] ^= 0x01;

        await Assert.ThrowsAsync<AuthenticationTagMismatchException>(
            () => provider.DecryptAsync(
                new ContentDecryptionRequest
                {
                    Ciphertext = encrypted.Ciphertext,
                    Nonce = encrypted.Nonce,
                    AuthenticationTag = encrypted.AuthenticationTag,
                    KeyId = encrypted.KeyId
                }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(31)]
    [InlineData(33)]
    public async Task Operations_RejectNon256BitKey(
        int keyLength)
    {
        var key =
            new EncryptionKey
            {
                KeyId = "key-001",
                KeyMaterial = new byte[keyLength]
            };

        var provider =
            new DevelopmentContentCryptographyProvider(
                new InMemoryEncryptionKeyProvider(
                    new[] { key }));

        await Assert.ThrowsAsync<CryptographicException>(
            () => provider.EncryptAsync(
                Encoding.UTF8.GetBytes("protected")));

        await Assert.ThrowsAsync<CryptographicException>(
            () => provider.DecryptAsync(
                new ContentDecryptionRequest
                {
                    Ciphertext = [1],
                    Nonce = new byte[12],
                    AuthenticationTag = new byte[16],
                    KeyId = "key-001"
                }));
    }

    [Fact]
    public async Task Operations_RejectMismatchedReturnedKeyId()
    {
        var provider =
            new DevelopmentContentCryptographyProvider(
                new MismatchedKeyProvider());

        await Assert.ThrowsAsync<CryptographicException>(
            () => provider.EncryptAsync(
                Encoding.UTF8.GetBytes("protected")));

        await Assert.ThrowsAsync<CryptographicException>(
            () => provider.DecryptAsync(
                new ContentDecryptionRequest
                {
                    Ciphertext = [1],
                    Nonce = new byte[12],
                    AuthenticationTag = new byte[16],
                    KeyId = "requested-key"
                }));
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

            return Task.FromResult<string?>(
                "requested-key");
        }

        public Task<EncryptionKey?> GetKeyAsync(
            string keyId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<EncryptionKey?>(
                _key);
        }
    }
}
