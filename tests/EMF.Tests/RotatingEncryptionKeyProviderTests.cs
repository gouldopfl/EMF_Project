using System.Security.Cryptography;
using System.Text;
using EMF.Security.Encryption;
using EMF.Security.Encryption.Models;
using EMF.Security.Encryption.Services;

namespace EMF.Tests;

public sealed class RotatingEncryptionKeyProviderTests
{
    [Fact]
    public async Task RotatingCurrentKey_PreservesAccessToPreviouslyEncryptedContent()
    {
        var keyA =
            new EncryptionKey
            {
                KeyId = "key-A",
                KeyMaterial =
                    RandomNumberGenerator.GetBytes(32)
            };

        var keyB =
            new EncryptionKey
            {
                KeyId = "key-B",
                KeyMaterial =
                    RandomNumberGenerator.GetBytes(32)
            };

        var provider =
            new RotatingTestKeyProvider(
                keyA,
                keyB);

        var service =
            new DevelopmentEncryptionService(provider);

        var plaintextA =
            Encoding.UTF8.GetBytes("Encrypted with key A.");

        var encryptedA =
            await service.EncryptAsync(plaintextA);

        Assert.Equal("key-A", encryptedA.KeyId);

        provider.RotateTo("key-B");

        var decryptedA =
            await service.DecryptAsync(encryptedA);

        Assert.Equal(plaintextA, decryptedA);

        var plaintextB =
            Encoding.UTF8.GetBytes("Encrypted with key B.");

        var encryptedB =
            await service.EncryptAsync(plaintextB);

        Assert.Equal("key-B", encryptedB.KeyId);

        var decryptedB =
            await service.DecryptAsync(encryptedB);

        Assert.Equal(plaintextB, decryptedB);
    }

    private sealed class RotatingTestKeyProvider :
        IEncryptionKeyProvider
    {
        private readonly IReadOnlyDictionary<string, EncryptionKey> _keys;
        private string _currentKeyId;

        public RotatingTestKeyProvider(
            params EncryptionKey[] keys)
        {
            if (keys.Length == 0)
            {
                throw new ArgumentException(
                    "At least one key is required.",
                    nameof(keys));
            }

            _keys = keys.ToDictionary(
                key => key.KeyId,
                StringComparer.Ordinal);

            _currentKeyId = keys[0].KeyId;
        }

        public Task<string?> GetCurrentKeyIdAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<string?>(_currentKeyId);
        }

        public Task<EncryptionKey?> GetKeyAsync(
            string keyId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _keys.TryGetValue(keyId, out var key);

            return Task.FromResult(key);
        }

        public void RotateTo(string keyId)
        {
            if (!_keys.ContainsKey(keyId))
            {
                throw new ArgumentException(
                    "Unknown key.",
                    nameof(keyId));
            }

            _currentKeyId = keyId;
        }
    }
}
