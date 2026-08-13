using System.Security.Cryptography;
using EMF.Security.Encryption.Models;
using EMF.Security.Encryption.Services;

namespace EMF.Tests;

public sealed class InMemoryEncryptionKeyProviderTests
{
    [Fact]
    public async Task KnownKeyId_ReturnsKey()
    {
        var key =
            new EncryptionKey
            {
                KeyId = "key-001",
                KeyMaterial =
                    RandomNumberGenerator.GetBytes(32)
            };

        var provider =
            new InMemoryEncryptionKeyProvider(
                new[] { key });

        var result =
            await provider.GetKeyAsync("key-001");

        Assert.NotNull(result);
        Assert.Equal("key-001", result.KeyId);
        Assert.Equal(key.KeyMaterial, result.KeyMaterial);
    }

    [Fact]
    public async Task UnknownKeyId_ReturnsNull()
    {
        var provider =
            new InMemoryEncryptionKeyProvider(
                Array.Empty<EncryptionKey>());

        var result =
            await provider.GetKeyAsync("missing-key");

        Assert.Null(result);
    }


    [Fact]
    public async Task CurrentKeyId_ReturnsFirstConfiguredKey()
    {
        var provider =
            new InMemoryEncryptionKeyProvider(
                new[]
                {
                    new EncryptionKey
                    {
                        KeyId = "key-current",
                        KeyMaterial =
                            RandomNumberGenerator.GetBytes(32)
                    },
                    new EncryptionKey
                    {
                        KeyId = "key-old",
                        KeyMaterial =
                            RandomNumberGenerator.GetBytes(32)
                    }
                });

        var result =
            await provider.GetCurrentKeyIdAsync();

        Assert.Equal("key-current", result);
    }
}
