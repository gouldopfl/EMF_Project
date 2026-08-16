using System.Security.Cryptography;
using System.Text;
using EMF.Security.Encryption;
using EMF.Security.Encryption.Envelope.Services;
using EMF.Security.Encryption.Models;

namespace EMF.Tests;

public sealed class
    DevelopmentEnvelopeKeyRewrappingServiceTests
{
    [Fact]
    public async Task RewrapAsync_PreservesContentEncryption()
    {
        var keyA = CreateKey("kek-A");
        var keyB = CreateKey("kek-B");

        var provider =
            new RotatingKeyProvider(keyA, keyB);

        var encryption =
            new DevelopmentEnvelopeEncryptionService(
                provider);

        var plaintext =
            Encoding.UTF8.GetBytes(
                "Artifact content protected by KEK A.");

        var original =
            await encryption.EncryptAsync(plaintext);

        provider.RotateTo(keyB.KeyId);

        var rewrapping =
            new DevelopmentEnvelopeKeyRewrappingService(
                provider);

        var rewrapped =
            await rewrapping.RewrapAsync(original);

        Assert.Equal(
            keyB.KeyId,
            rewrapped.KeyEncryptionKeyId);

        Assert.Equal(
            original.Ciphertext,
            rewrapped.Ciphertext);

        Assert.Equal(
            original.Nonce,
            rewrapped.Nonce);

        Assert.Equal(
            original.AuthenticationTag,
            rewrapped.AuthenticationTag);

        Assert.Equal(
            original.Algorithm,
            rewrapped.Algorithm);

        Assert.Equal(
            original.FormatVersion,
            rewrapped.FormatVersion);

        Assert.NotEqual(
            original.WrappedDataEncryptionKey,
            rewrapped.WrappedDataEncryptionKey);

        Assert.Equal(
            plaintext,
            await encryption.DecryptAsync(rewrapped));

        Assert.Same(
            rewrapped,
            await rewrapping.RewrapAsync(rewrapped));
    }


    [Fact]
    public async Task RewrapAsync_MissingHistoricalKey_Fails()
    {
        var keyA = CreateKey("kek-A");
        var keyB = CreateKey("kek-B");

        var provider =
            new RotatingKeyProvider(keyA, keyB);

        var original =
            await new DevelopmentEnvelopeEncryptionService(
                provider)
                .EncryptAsync(
                    Encoding.UTF8.GetBytes(
                        "Protected artifact content."));

        provider.RotateTo(keyB.KeyId);
        provider.Remove(keyA.KeyId);

        var rewrapping =
            new DevelopmentEnvelopeKeyRewrappingService(
                provider);

        await Assert.ThrowsAsync<
            CryptographicException>(
                () => rewrapping.RewrapAsync(original));
    }
    private static EncryptionKey CreateKey(
        string keyId)
    {
        return new EncryptionKey
        {
            KeyId = keyId,
            KeyMaterial =
                RandomNumberGenerator.GetBytes(32)
        };
    }

    private sealed class RotatingKeyProvider :
        IEncryptionKeyProvider
    {
        private readonly Dictionary<
            string,
            EncryptionKey> _keys;

        private string _currentKeyId;

        public RotatingKeyProvider(
            params EncryptionKey[] keys)
        {
            _keys =
                keys.ToDictionary(
                    key => key.KeyId,
                    StringComparer.Ordinal);

            _currentKeyId = keys[0].KeyId;
        }

        public void Remove(string keyId)
        {
            _keys.Remove(keyId);
        }

        public void RotateTo(string keyId)
        {
            _currentKeyId = keyId;
        }

        public Task<string?> GetCurrentKeyIdAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            return Task.FromResult<string?>(
                _currentKeyId);
        }

        public Task<EncryptionKey?> GetKeyAsync(
            string keyId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            _keys.TryGetValue(keyId, out var key);

            return Task.FromResult(key);
        }
    }
}
