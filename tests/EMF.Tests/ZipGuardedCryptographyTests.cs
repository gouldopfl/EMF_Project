using EMF.Security.Azure.Cryptography;
using EMF.Security.Azure.Encryption;
using EMF.Security.Azure.Keys;
using System.Security.Cryptography;
using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts.Zip;
using EMF.Core.Models.Identities;
using EMF.Security.Encryption;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Models;
using EMF.Security.Encryption.Envelope.Services;
using EMF.Security.Encryption.Models;
using EMF.Security.Storage;

namespace EMF.Tests;

public sealed class ZipGuardedCryptographyTests
{
    [Fact]
    public async Task GuardedRoundTripRetainsExistingEnvelopeFormatAndAuthenticatedContext()
    {
        using var guard = new Guard();
        var service = new DevelopmentEnvelopeEncryptionService(new Keys());
        var context = "context"u8.ToArray();
        var envelope = await service.EncryptWithContextGuardedAsync("payload"u8.ToArray(), context, guard);
        Assert.Equal(2, envelope.FormatVersion);
        Assert.Equal("payload"u8.ToArray(), await service.DecryptWithContextBoundedGuardedAsync(envelope, context, new(7), guard));
        await Assert.ThrowsAnyAsync<CryptographicException>(() =>
            service.DecryptWithContextBoundedGuardedAsync(envelope, "different-context"u8.ToArray(), new(7), guard));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidationWhileProviderWaitsDeniesCryptoBeforeUnwrapOrPayloadWork(bool decrypt)
    {
        using var guard = new Guard();
        var keys = new Keys();
        var service = new DevelopmentEnvelopeEncryptionService(keys);
        var context = "context"u8.ToArray();
        var envelope = await service.EncryptWithContextAsync("payload"u8.ToArray(), context);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        keys.OnKey = async () => { reached.SetResult(); await release.Task; };
        int unwraps = 0;
        service.BoundedDataKeyUnwrapped = _ => unwraps++;
        Task work = decrypt
            ? service.DecryptWithContextBoundedGuardedAsync(envelope, context, new(7), guard)
            : service.EncryptWithContextGuardedAsync("payload"u8.ToArray(), context, guard);
        await reached.Task;
        guard.Revoke();
        release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
        Assert.Equal(0, unwraps);
    }

    [Fact]
    public async Task InvalidationAfterUnwrapClearsDataKeyAndDeniesPlaintext()
    {
        using var guard = new Guard();
        var service = new DevelopmentEnvelopeEncryptionService(new Keys());
        var context = "context"u8.ToArray();
        var envelope = await service.EncryptWithContextAsync("payload"u8.ToArray(), context);
        byte[]? key = null;
        service.BoundedDataKeyUnwrapped = bytes => { key = bytes; guard.Revoke(); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.DecryptWithContextBoundedGuardedAsync(envelope, context, new(7), guard));
        Assert.NotNull(key);
        Assert.All(key!, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task RawLeaseDisposalInvalidationDeniesEncryptedHandoffAndClearsPlaintext()
    {
        using var guard = new Guard();
        var id = new ArtifactId("guarded-source");
        var crypto = new CapturingCrypto(new DevelopmentEnvelopeEncryptionService(new Keys()));
        var envelope = await crypto.EncryptWithContextAsync("payload"u8.ToArray(), ArtifactEnvelopeContext.Create(id));
        var physical = new Physical(id, BoundedEncryptedEnvelopeCodec.Write(envelope, new(7)))
            { AfterDispose = guard.Revoke };
        var encrypted = new EncryptedArtifactContentStore(physical, crypto);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            encrypted.ReadBoundedVersionedGuardedAsync(id, new(4096, 7, physical.Revision), guard));
        Assert.Equal(1, physical.GuardedReads);
        Assert.Equal(1, physical.Disposals);
        Assert.NotNull(crypto.Plaintext);
        Assert.All(crypto.Plaintext!, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task GuardedEncryptedReadRejectsUnsupportedPhysicalCapabilityBeforeRead()
    {
        using var guard = new Guard();
        var physical = new UnsupportedPhysical();
        var encrypted = new EncryptedArtifactContentStore(physical, new DevelopmentEnvelopeEncryptionService(new Keys()));
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            encrypted.ReadBoundedVersionedGuardedAsync(new("source"), new(4096, 7), guard));
        Assert.Equal(0, physical.Reads);
    }

    [Fact]
    public async Task AzureUnwrapWaitInvalidationClearsReturnedDataKeyAndDeniesPlaintext()
    {
        using var guard = new Guard();
        var cryptography = new AzureCrypto();
        var service = new AzureEnvelopeEncryptionService(new AzureKeys(), new AzureFactory(cryptography));
        var context = "context"u8.ToArray();
        var envelope = await service.EncryptWithContextAsync("payload"u8.ToArray(), context);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cryptography.OnUnwrap = async () => { reached.SetResult(); await release.Task; };
        var work = service.DecryptWithContextBoundedGuardedAsync(envelope, context, new(7), guard);
        await reached.Task;
        guard.Revoke();
        release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
        Assert.NotNull(cryptography.Unwrapped);
        Assert.All(cryptography.Unwrapped!, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task AzureWrapWaitInvalidationClearsKeyAndDeniesEnvelope()
    {
        using var guard = new Guard();
        var cryptography = new AzureCrypto();
        var service = new AzureEnvelopeEncryptionService(new AzureKeys(), new AzureFactory(cryptography));
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cryptography.OnWrap = async () => { reached.SetResult(); await release.Task; };
        var work = service.EncryptWithContextGuardedAsync("payload"u8.ToArray(), "context"u8.ToArray(), guard);
        await reached.Task;
        guard.Revoke();
        release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
        Assert.NotNull(cryptography.WrappingKey);
        Assert.NotNull(cryptography.Wrapped);
        Assert.All(cryptography.WrappingKey!, value => Assert.Equal(0, value));
        Assert.All(cryptography.Wrapped!, value => Assert.Equal(0, value));
    }

    private sealed class AzureKeys : IAzureKeyReferenceProvider
    {
        private readonly AzureKeyReference key = new() { KeyName = "key", KeyVersion = "v1" };
        public Task<AzureKeyReference> GetCurrentKeyAsync(CancellationToken ct = default) => Task.FromResult(key);
        public Task<AzureKeyReference?> GetKeyAsync(string id, CancellationToken ct = default) => Task.FromResult<AzureKeyReference?>(key);
    }
    private sealed class AzureFactory(AzureCrypto cryptography) : IAzureKeyCryptographyFactory
    { public IAzureKeyCryptography Create(AzureKeyReference key) => cryptography; }
    private sealed class AzureCrypto : IAzureKeyCryptography
    {
        public Func<Task>? OnUnwrap, OnWrap;
        public byte[]? Unwrapped, WrappingKey, Wrapped;
        public async Task<byte[]> WrapKeyAsync(byte[] key, CancellationToken ct = default)
        {
            WrappingKey = key;
            Wrapped = (byte[])key.Clone();
            if (OnWrap is not null) await OnWrap();
            return Wrapped;
        }
        public async Task<byte[]> UnwrapKeyAsync(byte[] wrapped, CancellationToken ct = default)
        { if (OnUnwrap is not null) await OnUnwrap(); return Unwrapped = (byte[])wrapped.Clone(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NullAcceptanceCapabilityDeniesCryptoBeforePayloadOrUnwrap(bool decrypt)
    {
        using var guard = new Guard { NullAcceptance = true };
        var service = new DevelopmentEnvelopeEncryptionService(new Keys());
        var context = "context"u8.ToArray();
        var envelope = await service.EncryptWithContextAsync("payload"u8.ToArray(), context);
        int unwraps = 0;
        service.BoundedDataKeyUnwrapped = _ => unwraps++;
        Task work = decrypt
            ? service.DecryptWithContextBoundedGuardedAsync(envelope, context, new(7), guard)
            : service.EncryptWithContextGuardedAsync("payload"u8.ToArray(), context, guard);
        await Assert.ThrowsAnyAsync<UnauthorizedAccessException>(() => work);
        Assert.Equal(0,unwraps);
    }

    [Fact]
    public async Task NullAcceptanceCapabilityDeniesEncryptedCodecAndDecryptionAndDisposesRawLease()
    {
        using var guard = new Guard { NullAcceptance = true };
        var id = new ArtifactId("guarded-source");
        var crypto = new CapturingCrypto(new DevelopmentEnvelopeEncryptionService(new Keys()));
        var envelope = await crypto.EncryptWithContextAsync("payload"u8.ToArray(),ArtifactEnvelopeContext.Create(id));
        var physical = new Physical(id,BoundedEncryptedEnvelopeCodec.Write(envelope,new(7)));
        var encrypted = new EncryptedArtifactContentStore(physical,crypto);
        await Assert.ThrowsAnyAsync<UnauthorizedAccessException>(() =>
            encrypted.ReadBoundedVersionedGuardedAsync(id,new(4096,7,physical.Revision),guard));
        Assert.Equal(1,physical.GuardedReads);
        Assert.Equal(1,physical.Disposals);
        Assert.Null(crypto.Plaintext);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptanceDisposalFailureAfterAzureProviderWorkClearsReturnedKeys(bool encrypt)
    {
        int disposals=0;
        using var guard=new Guard { AfterAcceptance=()=> { if(++disposals==(encrypt?3:1))throw new IOException("Synthetic acceptance disposal failed."); } };
        var cryptography=new AzureCrypto();
        var service=new AzureEnvelopeEncryptionService(new AzureKeys(),new AzureFactory(cryptography));
        var context="context"u8.ToArray();
        var envelope=await service.EncryptWithContextAsync("payload"u8.ToArray(),context);
        var reached=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task ProviderWait(){reached.SetResult();await release.Task;}
        if(encrypt)cryptography.OnWrap=ProviderWait;else cryptography.OnUnwrap=ProviderWait;
        Task work=encrypt ? service.EncryptWithContextGuardedAsync("payload"u8.ToArray(),context,guard)
            : service.DecryptWithContextBoundedGuardedAsync(envelope,context,new(7),guard);
        await reached.Task;
        Assert.False(work.IsCompleted); // Accepted provider work must settle before buffers are cleared.
        if(encrypt)Assert.Contains(cryptography.WrappingKey!,value=>value!=0);
        release.SetResult();
        await Assert.ThrowsAnyAsync<IOException>(()=>work);
        var captured=encrypt?cryptography.WrappingKey:cryptography.Unwrapped;
        Assert.NotNull(captured);
        Assert.All(captured!,value=>Assert.Equal(0,value));
        if(encrypt)
        {
            Assert.NotNull(cryptography.Wrapped);
            Assert.All(cryptography.Wrapped!,value=>Assert.Equal(0,value));
        }
    }

    [Fact]
    public void AcceptanceDisposalFailureAfterDecryptionClearsAllocatedPlaintext()
    {
        var dek=Enumerable.Repeat((byte)7,32).ToArray();
        var context="context"u8.ToArray();
        var nonce=new byte[12];var ciphertext=new byte[7];var tag=new byte[16];
        using(var aes=new AesGcm(dek,16))aes.Encrypt(nonce,"payload"u8,ciphertext,tag,
            EncryptedEnvelopeFormat.GetContextBoundAuthenticatedData("AES-256-GCM",context));
        var envelope=new EncryptedEnvelope { FormatVersion=2,Ciphertext=ciphertext,Nonce=nonce,AuthenticationTag=tag,
            WrappedDataEncryptionKey=new byte[]{1},KeyEncryptionKeyId="key",Algorithm="AES-256-GCM" };
        int disposals=0;
        using var guard=new Guard { AfterAcceptance=()=> { if(++disposals==2)throw new IOException("Synthetic acceptance disposal failed."); } };
        byte[]? captured=null;
        Assert.ThrowsAny<IOException>(()=>EnvelopeContentAuthentication.DecryptBounded(envelope,dek,context,new(7),default,
            bytes=>captured=bytes,null,guard));
        Assert.NotNull(captured);
        Assert.All(captured!,value=>Assert.Equal(0,value));
    }

    private sealed class Guard : IZipEffectBoundaryGuard, IDisposable
    {
        private readonly CancellationTokenSource invalidated = new();
        public bool NullAcceptance;
        public Action? AfterAcceptance;
        public CancellationToken Invalidated => invalidated.Token;
        public void Revoke() => invalidated.Cancel();
        public ValueTask RevalidateAsync(CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); Invalidated.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; }
        public IDisposable Accept(CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); Invalidated.ThrowIfCancellationRequested(); return NullAcceptance ? null! : new Accepted(AfterAcceptance); }
        public void Dispose() => invalidated.Dispose();
        private sealed class Accepted(Action? after) : IDisposable { public void Dispose() => after?.Invoke(); }
    }
    private sealed class Keys : IEncryptionKeyProvider
    {
        public Func<Task>? OnKey;
        private readonly EncryptionKey key = new() { KeyId = "key", KeyMaterial = Enumerable.Repeat((byte)7, 32).ToArray() };
        public Task<string?> GetCurrentKeyIdAsync(CancellationToken ct = default) => Task.FromResult<string?>(key.KeyId);
        public async Task<EncryptionKey?> GetKeyAsync(string id, CancellationToken ct = default)
        { if (OnKey is not null) await OnKey(); return key; }
    }
    private class UnsupportedPhysical : IArtifactContentStore, IBoundedVersionedArtifactContentStore
    {
        public int Reads;
        public long MaximumStoredRepresentationBytes => 4096;
        public Task<IArtifactContentReadLease?> ReadBoundedVersionedAsync(ArtifactId id, BoundedArtifactContentReadRequest request, CancellationToken ct = default)
        { Reads++; throw new InvalidOperationException("Raw read must not be selected."); }
        public Task WriteAsync(ArtifactId id, ReadOnlyMemory<byte> content, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<byte[]?> ReadAsync(ArtifactId id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(ArtifactId id, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class Physical(ArtifactId id, byte[] content) : UnsupportedPhysical, IZipGuardedBoundedArtifactContentStore
    {
        public ArtifactContentRevision Revision { get; } = new("source-revision");
        public Action? AfterDispose;
        public int GuardedReads, Disposals;
        public Task<IArtifactContentReadLease?> ReadBoundedVersionedGuardedAsync(ArtifactId requested,
            BoundedArtifactContentReadRequest request, IZipEffectBoundaryGuard guard, CancellationToken ct = default)
        {
            using (guard.Accept(ct)) { GuardedReads++; }
            return Task.FromResult<IArtifactContentReadLease?>(new Lease(this, id, content));
        }
        private sealed class Lease(Physical owner, ArtifactId id, byte[] content) : IArtifactContentReadLease
        {
            public ArtifactId ArtifactId => id;
            public ArtifactContentRevision Revision => owner.Revision;
            public long StoredLength => content.Length;
            public long ReturnedLength => content.Length;
            public ReadOnlyMemory<byte> Content => content;
            public Stream OpenReadStream() => new MemoryStream(content, false);
            public void Dispose() { owner.Disposals++; owner.AfterDispose?.Invoke(); }
            public async ValueTask DisposeAsync() { await Task.Yield(); Dispose(); }
        }
    }
    private sealed class CapturingCrypto(DevelopmentEnvelopeEncryptionService inner) : IEnvelopeEncryptionService,
        IBoundedEnvelopeDecryptionService, IZipGuardedEnvelopeEncryptionService
    {
        public byte[]? Plaintext;
        public Task<EncryptedEnvelope> EncryptAsync(ReadOnlyMemory<byte> bytes, CancellationToken ct = default) => inner.EncryptAsync(bytes, ct);
        public Task<byte[]> DecryptAsync(EncryptedEnvelope envelope, CancellationToken ct = default) => inner.DecryptAsync(envelope, ct);
        public Task<EncryptedEnvelope> EncryptWithContextAsync(ReadOnlyMemory<byte> bytes, ReadOnlyMemory<byte> context, CancellationToken ct = default) => inner.EncryptWithContextAsync(bytes, context, ct);
        public Task<byte[]> DecryptWithContextAsync(EncryptedEnvelope envelope, ReadOnlyMemory<byte> context, CancellationToken ct = default) => inner.DecryptWithContextAsync(envelope, context, ct);
        public Task<byte[]> DecryptWithContextBoundedAsync(EncryptedEnvelope envelope, ReadOnlyMemory<byte> context, EnvelopeDecryptionLimits limits, CancellationToken ct = default) => inner.DecryptWithContextBoundedAsync(envelope, context, limits, ct);
        public Task<EncryptedEnvelope> EncryptWithContextGuardedAsync(ReadOnlyMemory<byte> bytes, ReadOnlyMemory<byte> context, IZipEffectBoundaryGuard guard, CancellationToken ct = default) => inner.EncryptWithContextGuardedAsync(bytes, context, guard, ct);
        public async Task<byte[]> DecryptWithContextBoundedGuardedAsync(EncryptedEnvelope envelope, ReadOnlyMemory<byte> context,
            EnvelopeDecryptionLimits limits, IZipEffectBoundaryGuard guard, CancellationToken ct = default)
            => Plaintext = await inner.DecryptWithContextBoundedGuardedAsync(envelope, context, limits, guard, ct);
    }
}
