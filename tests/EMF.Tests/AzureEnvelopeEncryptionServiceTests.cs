using System.Security.Cryptography;
using System.Text;
using EMF.Security.Azure.Cryptography;
using EMF.Security.Azure.Encryption;
using EMF.Security.Azure.Keys;
using EMF.Security.Encryption.Envelope;

namespace EMF.Tests;

public sealed class AzureEnvelopeEncryptionServiceTests
{
    [Theory]
    [InlineData("success")] [InlineData("authentication")] [InlineData("cancellation")]
    public async Task BoundedProviderAlwaysClearsDekAcrossAsynchronousUnwrap(string outcome)
    {
        using var cts = new CancellationTokenSource();
        var cryptography = new CapturingBoundedCryptography();
        var service = new AzureEnvelopeEncryptionService(
            new FakeKeyProvider(new AzureKeyReference { KeyName = "emf-key", KeyVersion = "v1" }), new FakeFactory(cryptography));
        var context = "context"u8.ToArray();
        var envelope = await service.EncryptWithContextAsync(new byte[] { 1, 2, 3 }, context);
        if (outcome == "authentication") envelope.AuthenticationTag[0] ^= 1;
        cryptography.AfterUnwrap = () => { if (outcome == "cancellation") cts.Cancel(); };
        if (outcome == "authentication")
            await Assert.ThrowsAnyAsync<CryptographicException>(() => service.DecryptWithContextBoundedAsync(envelope, context, new(3), cts.Token));
        else if (outcome == "cancellation")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DecryptWithContextBoundedAsync(envelope, context, new(3), cts.Token));
        else Assert.Equal(new byte[] { 1, 2, 3 }, await service.DecryptWithContextBoundedAsync(envelope, context, new(3), cts.Token));
        Assert.True(cryptography.NonzeroAtUnwrap); Assert.NotNull(cryptography.OwnedDek);
        Assert.All(cryptography.OwnedDek!, b => Assert.Equal(0, b));
    }
    private sealed class CapturingBoundedCryptography : IAzureKeyCryptography
    {
        public byte[]? OwnedDek; public bool NonzeroAtUnwrap; public Action? AfterUnwrap;
        public Task<byte[]> WrapKeyAsync(byte[] key, CancellationToken ct = default) => Task.FromResult((byte[])key.Clone());
        public async Task<byte[]> UnwrapKeyAsync(byte[] key, CancellationToken ct = default)
        {
            await Task.Yield();
            OwnedDek = (byte[])key.Clone(); NonzeroAtUnwrap = OwnedDek.Any(b => b != 0);
            AfterUnwrap?.Invoke(); return OwnedDek;
        }
    }

    [Fact]
    public async Task BoundedProviderRoundTripUsesExistingFormatAndAadWithoutLiveServices()
    {
        var service = new AzureEnvelopeEncryptionService(
            new FakeKeyProvider(new AzureKeyReference { KeyName = "emf-key", KeyVersion = "v1" }),
            new FakeFactory(new FakeCryptography()));
        var context = "artifact-context"u8.ToArray();
        var envelope = await service.EncryptWithContextAsync("protected"u8.ToArray(), context);
        Assert.Equal("protected"u8.ToArray(), await service.DecryptWithContextBoundedAsync(envelope, context, new(9)));
        Assert.Equal("protected"u8.ToArray(), await service.DecryptWithContextAsync(envelope, context));
        await Assert.ThrowsAsync<CryptographicException>(() => service.DecryptWithContextBoundedAsync(envelope, context, new(8)));
        await Assert.ThrowsAsync<CryptographicException>(() => service.DecryptWithContextBoundedAsync(envelope, context, new(9, 1)));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DecryptWithContextBoundedAsync(envelope, context, new(9), cts.Token));
    }

    [Fact]
    public async Task EncryptAsync_RejectsNullCryptographyFactoryResult()
    {
        var service = new AzureEnvelopeEncryptionService(
            new FakeKeyProvider(new AzureKeyReference
            {
                KeyName = "emf-key",
                KeyVersion = "v1"
            }),
            new NullFactory());

        await Assert.ThrowsAsync<CryptographicException>(
            () => service.EncryptAsync(
                Encoding.UTF8.GetBytes("protected")));
    }

    private sealed class NullFactory : IAzureKeyCryptographyFactory
    {
        public IAzureKeyCryptography Create(
            AzureKeyReference keyReference) => null!;
    }

    [Fact]
    public async Task EncryptThenDecrypt_RoundTripsPlaintext()
    {
        var keyReference =
            new AzureKeyReference
            {
                KeyName = "emf-key",
                KeyVersion = "v1"
            };

        var cryptography = new FakeCryptography();

        var service =
            new AzureEnvelopeEncryptionService(
                new FakeKeyProvider(keyReference),
                new FakeFactory(cryptography));

        var plaintext = Encoding.UTF8.GetBytes("hello emf");

        var envelope =
            await service.EncryptAsync(plaintext);

        var result =
            await service.DecryptAsync(envelope);

        Assert.Equal(plaintext, result);
        Assert.Equal(
            EncryptedEnvelopeFormat.CurrentVersion,
            envelope.FormatVersion);
        Assert.Equal(
            EncryptedEnvelopeFormat.Aes256GcmAlgorithm,
            envelope.Algorithm);
    }


    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(31)]
    [InlineData(33)]
    public async Task DecryptAsync_RejectsNon256BitDataEncryptionKey(
        int keyLength)
    {
        var keyReference =
            new AzureKeyReference
            {
                KeyName = "emf-key",
                KeyVersion = "v1"
            };

        var cryptography =
            new FakeCryptography(
                new byte[keyLength]);

        var service =
            new AzureEnvelopeEncryptionService(
                new FakeKeyProvider(keyReference),
                new FakeFactory(cryptography));

        var envelope =
            await service.EncryptAsync(
                Encoding.UTF8.GetBytes("protected"));

        await Assert.ThrowsAsync<CryptographicException>(
            () => service.DecryptAsync(envelope));
    }


    [Fact]
    public async Task DecryptAsync_RejectsWrongReturnedKeyIdentity()
    {
        var cryptography =
            new FakeCryptography();

        var encryptingService =
            new AzureEnvelopeEncryptionService(
                new FakeKeyProvider(
                    new AzureKeyReference
                    {
                        KeyName = "emf-key",
                        KeyVersion = "v1"
                    }),
                new FakeFactory(cryptography));

        var envelope =
            await encryptingService.EncryptAsync(
                Encoding.UTF8.GetBytes("protected"));

        var decryptingService =
            new AzureEnvelopeEncryptionService(
                new FakeKeyProvider(
                    new AzureKeyReference
                    {
                        KeyName = "emf-key",
                        KeyVersion = "v2"
                    }),
                new FakeFactory(cryptography));

        await Assert.ThrowsAsync<CryptographicException>(
            () => decryptingService.DecryptAsync(envelope));
    }

    [Fact]
    public async Task DecryptAsync_RejectsNullCryptographyFactoryResult()
    {
        var key = new AzureKeyReference
        {
            KeyName = "emf-key",
            KeyVersion = "v1"
        };

        var envelope = await new AzureEnvelopeEncryptionService(
            new FakeKeyProvider(key),
            new FakeFactory(new FakeCryptography()))
            .EncryptAsync(Encoding.UTF8.GetBytes("protected"));

        var service = new AzureEnvelopeEncryptionService(
            new FakeKeyProvider(key),
            new NullFactory());

        await Assert.ThrowsAsync<CryptographicException>(
            () => service.DecryptAsync(envelope));
    }

    [Fact]
    public async Task DecryptWithContextAsync_RejectsWrongContext()
    {
        var keyReference = new AzureKeyReference
        {
            KeyName = "emf-key",
            KeyVersion = "v1"
        };

        var service =
            new AzureEnvelopeEncryptionService(
                new FakeKeyProvider(keyReference),
                new FakeFactory(new FakeCryptography()));

        var envelope =
            await service.EncryptWithContextAsync(
                Encoding.UTF8.GetBytes("protected"),
                Encoding.UTF8.GetBytes("artifact-a"));

        Assert.Equal(
            EncryptedEnvelopeFormat.ContextBoundVersion,
            envelope.FormatVersion);

        await Assert.ThrowsAnyAsync<CryptographicException>(
            () => service.DecryptWithContextAsync(
                envelope,
                Encoding.UTF8.GetBytes("artifact-b")));
    }

    [Fact]
    public async Task DecryptAsync_UsesStoredHistoricalIdentityAfterRotation()
    {
        var keys = new HistoricalKeyProvider();
        var service = new AzureEnvelopeEncryptionService(keys, new FakeFactory(new FakeCryptography()));
        byte[] content = [1, 2, 3];
        var historical = await service.EncryptAsync(content);
        keys.CurrentVersion = "v2";
        var current = await service.EncryptAsync(content);

        Assert.Equal("emf-key/v1", historical.KeyEncryptionKeyId);
        Assert.Equal("emf-key/v2", current.KeyEncryptionKeyId);
        var decrypted = await service.DecryptAsync(historical);
        Assert.True(content.SequenceEqual(decrypted));
        Assert.Equal("emf-key/v1", keys.LastRequestedId);
        keys.HistoricalAvailable = false;
        await Assert.ThrowsAsync<CryptographicException>(() => service.DecryptAsync(historical));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EncryptAsync_WrapFailureOrCancellationClearsTemporaryDek(bool cancelled)
    {
        var cryptography = new FailingWrapCryptography(cancelled);
        var service = new AzureEnvelopeEncryptionService(
            new FakeKeyProvider(new AzureKeyReference { KeyName = "emf-key", KeyVersion = "v1" }),
            new FakeFactory(cryptography));

        var failure = await Record.ExceptionAsync(() => service.EncryptAsync(new byte[] { 1, 2, 3 }));

        Assert.NotNull(failure);
        if (cancelled)
            Assert.IsAssignableFrom<OperationCanceledException>(failure);
        else
        {
            var sanitized = Assert.IsType<EMF.Security.Encryption.Envelope.EnvelopeProviderFailure>(failure);
            Assert.Equal(EMF.Security.Encryption.Envelope.EnvelopeFailureCategory.UnknownProviderFailure, sanitized.Category);
            Assert.Null(sanitized.InnerException);
        }
        Assert.NotNull(cryptography.TemporaryDek);
        Assert.True(cryptography.TemporaryDek!.All(value => value == 0));
    }

    private sealed class HistoricalKeyProvider : IAzureKeyReferenceProvider
    {
        public string CurrentVersion { get; set; } = "v1";
        public bool HistoricalAvailable { get; set; } = true;
        public string? LastRequestedId { get; private set; }
        public Task<AzureKeyReference> GetCurrentKeyAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new AzureKeyReference { KeyName = "emf-key", KeyVersion = CurrentVersion });
        }
        public Task<AzureKeyReference?> GetKeyAsync(string keyIdentifier, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequestedId = keyIdentifier;
            var version = keyIdentifier.Split('/')[1];
            return Task.FromResult<AzureKeyReference?>(
                version == "v1" && !HistoricalAvailable ? null :
                new AzureKeyReference { KeyName = "emf-key", KeyVersion = version });
        }
    }

    private sealed class FailingWrapCryptography(bool cancelled) : IAzureKeyCryptography
    {
        public byte[]? TemporaryDek { get; private set; }
        public Task<byte[]> WrapKeyAsync(byte[] key, CancellationToken cancellationToken = default)
        {
            TemporaryDek = key;
            return Task.FromException<byte[]>(cancelled ?
                new OperationCanceledException("Synthetic wrapping cancellation.") :
                new InvalidOperationException("Synthetic wrapping failure."));
        }
        public Task<byte[]> UnwrapKeyAsync(byte[] wrappedKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeKeyProvider :
        IAzureKeyReferenceProvider
    {
        private readonly AzureKeyReference _reference;

        public FakeKeyProvider(AzureKeyReference reference)
        {
            _reference = reference;
        }

        public Task<AzureKeyReference> GetCurrentKeyAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_reference);

        public Task<AzureKeyReference?> GetKeyAsync(
            string keyIdentifier,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AzureKeyReference?>(_reference);
    }

    private sealed class FakeFactory :
        IAzureKeyCryptographyFactory
    {
        private readonly IAzureKeyCryptography _cryptography;

        public FakeFactory(IAzureKeyCryptography cryptography)
        {
            _cryptography = cryptography;
        }

        public IAzureKeyCryptography Create(
            AzureKeyReference keyReference) =>
            _cryptography;
    }

    private sealed class FakeCryptography :
        IAzureKeyCryptography
    {
        private readonly byte[]? _unwrappedKey;

        public FakeCryptography(
            byte[]? unwrappedKey = null)
        {
            _unwrappedKey = unwrappedKey;
        }

        public Task<byte[]> WrapKeyAsync(
            byte[] key,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(key.ToArray());

        public Task<byte[]> UnwrapKeyAsync(
            byte[] wrappedKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                (_unwrappedKey ?? wrappedKey).ToArray());
    }
}
