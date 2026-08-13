using System.Security.Cryptography;
using System.Text;
using EMF.Security.Encryption;
using EMF.Security.Encryption.Envelope.Services;
using EMF.Security.Encryption.Models;

namespace EMF.Tests;

public sealed class DevelopmentEnvelopeEncryptionRotationTests
{
    [Fact]
    public async Task KeyRotation_PreservesHistoricalAndNewContent()
    {
        var keyA =
            new EncryptionKey
            {
                KeyId = "kek-A",
                KeyMaterial = RandomNumberGenerator.GetBytes(32)
            };

        var keyB =
            new EncryptionKey
            {
                KeyId = "kek-B",
                KeyMaterial = RandomNumberGenerator.GetBytes(32)
            };

        var provider =
            new RotatingTestKeyProvider(keyA, keyB);

        var service =
            new DevelopmentEnvelopeEncryptionService(provider);

        var plaintextA =
            Encoding.UTF8.GetBytes("Encrypted with KEK A.");

        var encryptedA =
            await service.EncryptAsync(plaintextA);

        Assert.Equal("kek-A", encryptedA.KeyEncryptionKeyId);

        provider.RotateTo("kek-B");

        var decryptedA =
            await service.DecryptAsync(encryptedA);

        Assert.Equal(plaintextA, decryptedA);

        var plaintextB =
            Encoding.UTF8.GetBytes("Encrypted with KEK B.");

        var encryptedB =
            await service.EncryptAsync(plaintextB);

        Assert.Equal("kek-B", encryptedB.KeyEncryptionKeyId);

        var decryptedB =
            await service.DecryptAsync(encryptedB);

        Assert.Equal(plaintextB, decryptedB);
    }

    private sealed class RotatingTestKeyProvider :
        IEncryptionKeyProvider
    {
        private readonly Dictionary<string, EncryptionKey> _keys;
        private string _currentKeyId;

        public RotatingTestKeyProvider(
            EncryptionKey keyA,
            EncryptionKey keyB)
        {
            _keys = new Dictionary<string, EncryptionKey>(
                StringComparer.Ordinal)
            {
                [keyA.KeyId] = keyA,
                [keyB.KeyId] = keyB
            };

            _currentKeyId = keyA.KeyId;
        }

        public void RotateTo(string keyId)
        {
            _currentKeyId = keyId;
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

            _keys.TryGetValue(
                keyId,
                out var key);

            return Task.FromResult(key);
        }
    }
}
