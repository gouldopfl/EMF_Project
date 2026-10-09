using System.Security.Cryptography;
using System.Text;
using EMF.Security.Encryption;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Models;
using EMF.Security.Encryption.Envelope.Services;
using EMF.Security.Encryption.Models;
using EMF.Security.Encryption.Services;

namespace EMF.Tests;

public sealed class DevelopmentEnvelopeEncryptionServiceTests
{
    [Theory]
    [InlineData("success")] [InlineData("authentication")] [InlineData("cancellation")]
    public async Task BoundedProviderAlwaysClearsUnwrappedDek(string outcome)
    {
        var service = CreateService(); var context = "context"u8.ToArray();
        var envelope = await service.EncryptWithContextAsync(new byte[] { 1, 2, 3 }, context);
        if (outcome == "authentication") envelope.AuthenticationTag[0] ^= 1;
        byte[]? captured = null; bool nonzeroAtUnwrap = false;
        using var cts = new CancellationTokenSource();
        service.BoundedDataKeyUnwrapped = dek =>
        {
            captured = dek; nonzeroAtUnwrap = dek.Any(b => b != 0);
            if (outcome == "cancellation") cts.Cancel();
        };
        if (outcome == "authentication")
            await Assert.ThrowsAnyAsync<CryptographicException>(() => service.DecryptWithContextBoundedAsync(envelope, context, new(3), cts.Token));
        else if (outcome == "cancellation")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DecryptWithContextBoundedAsync(envelope, context, new(3), cts.Token));
        else Assert.Equal(new byte[] { 1, 2, 3 }, await service.DecryptWithContextBoundedAsync(envelope, context, new(3), cts.Token));
        Assert.True(nonzeroAtUnwrap); Assert.NotNull(captured); Assert.All(captured!, b => Assert.Equal(0, b));
    }

    [Fact]
    public async Task BoundedProviderRoundTripAndLegacyContextReadRemainCompatible()
    {
        var service = CreateService();
        var context = "artifact-context"u8.ToArray();
        var envelope = await service.EncryptWithContextAsync("protected"u8.ToArray(), context);
        Assert.Equal("protected"u8.ToArray(), await service.DecryptWithContextBoundedAsync(envelope, context, new(9)));
        Assert.Equal("protected"u8.ToArray(), await service.DecryptWithContextAsync(envelope, context));
        await Assert.ThrowsAsync<CryptographicException>(() => service.DecryptWithContextBoundedAsync(envelope, context, new(8)));
    }
    private static (EncryptedEnvelope Envelope, byte[] Dek, byte[] Context) BoundedFixture()
    {
        var dek = new byte[32]; var context = "context"u8.ToArray();
        var cipher = new byte[3]; var nonce = new byte[12]; var tag = new byte[16];
        using (var aes = new AesGcm(dek, 16))
            aes.Encrypt(nonce, new byte[] { 1, 2, 3 }, cipher, tag,
                EncryptedEnvelopeFormat.GetContextBoundAuthenticatedData("AES-256-GCM", context));
        return (new EncryptedEnvelope { FormatVersion = 2, Ciphertext = cipher, Nonce = nonce,
            AuthenticationTag = tag, WrappedDataEncryptionKey = new byte[] { 1 }, KeyEncryptionKeyId = "key",
            Algorithm = "AES-256-GCM" }, dek, context);
    }
    [Fact]
    public void BoundedPlaintextAndContextCeilingsRejectBeforeAllocation()
    {
        var f = BoundedFixture(); int allocations = 0;
        Assert.Throws<CryptographicException>(() => EnvelopeContentAuthentication.DecryptBounded(f.Envelope,
            f.Dek, f.Context, new(2), default, _ => allocations++, null));
        Assert.Throws<CryptographicException>(() => EnvelopeContentAuthentication.DecryptBounded(f.Envelope,
            f.Dek, f.Context, new(3, 1), default, _ => allocations++, null));
        Assert.Equal(0, allocations);
    }
    [Fact]
    public void BoundedAuthenticationFailureClearsActualAllocatedPlaintext()
    {
        var f = BoundedFixture(); f.Envelope.AuthenticationTag[0] ^= 1;
        byte[]? captured = null;
        Assert.ThrowsAny<CryptographicException>(() => EnvelopeContentAuthentication.DecryptBounded(f.Envelope,
            f.Dek, f.Context, new(3), default, bytes => { captured = bytes; Array.Fill(bytes, (byte)9); }, null));
        Assert.NotNull(captured); Assert.All(captured!, b => Assert.Equal(0, b));
    }
    [Theory]
    [InlineData("before")] [InlineData("allocated")] [InlineData("decrypted")]
    public void BoundedCancellationClearsBeforeHandoff(string point)
    {
        var f = BoundedFixture(); using var cts = new CancellationTokenSource(); byte[]? captured = null;
        if (point == "before") cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => EnvelopeContentAuthentication.DecryptBounded(f.Envelope,
            f.Dek, f.Context, new(3), cts.Token,
            bytes => { captured = bytes; Array.Fill(bytes, (byte)9); if (point == "allocated") cts.Cancel(); },
            () => { if (point == "decrypted") cts.Cancel(); }));
        if (point == "before") Assert.Null(captured);
        else { Assert.NotNull(captured); Assert.All(captured!, b => Assert.Equal(0, b)); }
    }
    [Fact]
    public void BoundedSuccessfulDecryptHandsOffObservedArrayOnce()
    {
        var f = BoundedFixture(); byte[]? captured = null; int allocations = 0;
        var result = EnvelopeContentAuthentication.DecryptBounded(f.Envelope, f.Dek, f.Context, new(3), default,
            bytes => { captured = bytes; allocations++; }, null);
        Assert.Same(captured, result); Assert.Equal(1, allocations); Assert.Equal(new byte[] { 1, 2, 3 }, result);
        CryptographicOperations.ZeroMemory(result);
    }

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
            EncryptedEnvelopeFormat.CurrentVersion,
            encrypted.FormatVersion);
        Assert.Equal(
            EncryptedEnvelopeFormat.Aes256GcmAlgorithm,
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

    [Fact]
    public async Task DecryptAsync_TamperedAlgorithmFails()
    {
        var service = CreateService();
        var plaintext =
            Encoding.UTF8.GetBytes("Protected evidence.");

        var encrypted =
            await service.EncryptAsync(plaintext);

        var tampered =
            new EncryptedEnvelope
            {
                FormatVersion = encrypted.FormatVersion,
                Ciphertext = encrypted.Ciphertext,
                Nonce = encrypted.Nonce,
                AuthenticationTag = encrypted.AuthenticationTag,
                WrappedDataEncryptionKey =
                    encrypted.WrappedDataEncryptionKey,
                KeyEncryptionKeyId =
                    encrypted.KeyEncryptionKeyId,
                Algorithm = "AES-128-GCM"
            };

        await Assert.ThrowsAsync<CryptographicException>(
            () => service.DecryptAsync(tampered));
    }

    [Fact]
    public async Task DecryptAsync_DowngradedFormatFails()
    {
        var service = CreateService();
        var plaintext =
            Encoding.UTF8.GetBytes("Protected evidence.");

        var encrypted =
            await service.EncryptAsync(plaintext);

        var tampered =
            new EncryptedEnvelope
            {
                FormatVersion =
                    EncryptedEnvelopeFormat.LegacyVersion,
                Ciphertext = encrypted.Ciphertext,
                Nonce = encrypted.Nonce,
                AuthenticationTag = encrypted.AuthenticationTag,
                WrappedDataEncryptionKey =
                    encrypted.WrappedDataEncryptionKey,
                KeyEncryptionKeyId =
                    encrypted.KeyEncryptionKeyId,
                Algorithm = encrypted.Algorithm
            };

        await Assert.ThrowsAsync<AuthenticationTagMismatchException>(
            () => service.DecryptAsync(tampered));
    }

    [Fact]
    public async Task Operations_RejectMismatchedReturnedKeyId()
    {
        var validService = CreateService();
        var envelope =
            await validService.EncryptAsync(
                Encoding.UTF8.GetBytes("protected"));

        var mismatchedService =
            new DevelopmentEnvelopeEncryptionService(
                new MismatchedKeyProvider());

        await Assert.ThrowsAsync<CryptographicException>(
            () => mismatchedService.EncryptAsync(
                Encoding.UTF8.GetBytes("protected")));

        await Assert.ThrowsAsync<CryptographicException>(
            () => mismatchedService.DecryptAsync(
                envelope));
    }

    [Theory]
    [InlineData("ciphertext")]
    [InlineData("nonce")]
    [InlineData("tag")]
    [InlineData("wrapped-dek")]
    public async Task DecryptAsync_RejectsAuthenticatedEnvelopeTampering(string field)
    {
        var service = CreateService();
        var envelope = await service.EncryptAsync(new byte[] { 1, 2, 3 });
        var bytes = field switch
        {
            "ciphertext" => envelope.Ciphertext,
            "nonce" => envelope.Nonce,
            "tag" => envelope.AuthenticationTag,
            _ => envelope.WrappedDataEncryptionKey
        };
        bytes[0] ^= 1;

        await Assert.ThrowsAnyAsync<CryptographicException>(() => service.DecryptAsync(envelope));
    }

    [Fact]
    public async Task DecryptAsync_RejectsWrongKeyMaterialWithSameIdentity()
    {
        var envelope = await CreateService().EncryptAsync(new byte[] { 1, 2, 3 });
        var wrongKey = new EncryptionKey
        {
            KeyId = envelope.KeyEncryptionKeyId,
            KeyMaterial = RandomNumberGenerator.GetBytes(32)
        };
        var service = new DevelopmentEnvelopeEncryptionService(
            new InMemoryEncryptionKeyProvider([wrongKey]));
        try
        {
            await Assert.ThrowsAnyAsync<CryptographicException>(() => service.DecryptAsync(envelope));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrongKey.KeyMaterial);
        }
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

            return Task.FromResult<string?>("kek-001");
        }

        public Task<EncryptionKey?> GetKeyAsync(
            string keyId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<EncryptionKey?>(_key);
        }
    }
}
