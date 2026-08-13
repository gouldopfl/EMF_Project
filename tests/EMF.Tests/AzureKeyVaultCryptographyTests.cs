using Azure.Security.KeyVault.Keys.Cryptography;
using EMF.Security.Azure.Cryptography;

namespace EMF.Tests;

public sealed class AzureKeyVaultCryptographyTests
{
    [Fact]
    public async Task WrapKeyAsync_DelegatesToClient()
    {
        var expected =
            new byte[] { 1, 2, 3 };

        var client =
            new FakeAzureCryptographyClient
            {
                WrappedResult = expected
            };

        var service =
            new AzureKeyVaultCryptography(client);

        var key =
            new byte[] { 4, 5, 6 };

        var result =
            await service.WrapKeyAsync(key);

        Assert.Equal(expected, result);
        Assert.Equal(
            KeyWrapAlgorithm.RsaOaep256,
            client.LastWrapAlgorithm);
        Assert.Equal(key, client.LastWrapKey);
    }

    [Fact]
    public async Task UnwrapKeyAsync_DelegatesToClient()
    {
        var expected =
            new byte[] { 7, 8, 9 };

        var client =
            new FakeAzureCryptographyClient
            {
                UnwrappedResult = expected
            };

        var service =
            new AzureKeyVaultCryptography(client);

        var wrapped =
            new byte[] { 10, 11, 12 };

        var result =
            await service.UnwrapKeyAsync(wrapped);

        Assert.Equal(expected, result);
        Assert.Equal(
            KeyWrapAlgorithm.RsaOaep256,
            client.LastUnwrapAlgorithm);
        Assert.Equal(wrapped, client.LastUnwrapKey);
    }

    [Fact]
    public void Constructor_RejectsNullClient()
    {
        Assert.Throws<ArgumentNullException>(
            () => new AzureKeyVaultCryptography(null!));
    }

    private sealed class FakeAzureCryptographyClient :
        IAzureCryptographyClient
    {
        public byte[] WrappedResult { get; init; } = [];

        public byte[] UnwrappedResult { get; init; } = [];

        public KeyWrapAlgorithm? LastWrapAlgorithm { get; private set; }

        public KeyWrapAlgorithm? LastUnwrapAlgorithm { get; private set; }

        public byte[]? LastWrapKey { get; private set; }

        public byte[]? LastUnwrapKey { get; private set; }

        public Task<byte[]> WrapKeyAsync(
            KeyWrapAlgorithm algorithm,
            byte[] key,
            CancellationToken cancellationToken = default)
        {
            LastWrapAlgorithm = algorithm;
            LastWrapKey = key;

            return Task.FromResult(WrappedResult);
        }

        public Task<byte[]> UnwrapKeyAsync(
            KeyWrapAlgorithm algorithm,
            byte[] wrappedKey,
            CancellationToken cancellationToken = default)
        {
            LastUnwrapAlgorithm = algorithm;
            LastUnwrapKey = wrappedKey;

            return Task.FromResult(UnwrappedResult);
        }
    }
}
