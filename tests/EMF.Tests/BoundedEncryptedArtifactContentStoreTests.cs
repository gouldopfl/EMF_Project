using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Models;
using EMF.Security.Encryption.Envelope.Services;
using EMF.Security.Encryption.Models;
using EMF.Security.Encryption.Services;
using EMF.Security.Storage;
using System.Security.Cryptography;
using System.Text.Json;
namespace EMF.Tests;

public sealed class BoundedEncryptedArtifactContentStoreTests
{
    private static readonly ArtifactId Id = new("bounded-encrypted");
    private static DevelopmentEnvelopeEncryptionService Service() => new(new InMemoryEncryptionKeyProvider(
        new[] { new EncryptionKey { KeyId = "key", KeyMaterial = new byte[32] } }));
    private static async Task<(Physical Physical, Provider Provider, EncryptedArtifactContentStore Store)> Fixture()
    {
        var encryption = Service();
        var envelope = await encryption.EncryptWithContextAsync(new byte[] { 1, 2, 3 }, ArtifactEnvelopeContext.Create(Id));
        // Proves the new reader accepts existing serialized wire representation.
        var physical = new Physical(JsonSerializer.SerializeToUtf8Bytes(envelope));
        var provider = new Provider(encryption);
        return (physical, provider, new(physical, provider));
    }
    [Fact]
    public async Task BoundedCapabilitiesUsedWithExactEvidenceAndNoOrdinaryFallback()
    {
        var f = await Fixture();
        await using var lease = await f.Store.ReadBoundedVersionedAsync(Id, new(1000, 3, new("revision")));
        Assert.NotNull(lease); Assert.Equal(Id, lease!.ArtifactId); Assert.Equal(new ArtifactContentRevision("revision"), lease.Revision);
        Assert.Equal(f.Physical.Source.Length, lease.StoredLength); Assert.Equal(3, lease.ReturnedLength);
        Assert.Equal(new byte[] { 1, 2, 3 }, lease.Content.ToArray());
        Assert.Equal(1, f.Physical.Reads); Assert.Equal(1, f.Provider.Calls);
        Assert.Equal(1000, f.Physical.Request!.MaximumReturnedContentBytes);
        Assert.Equal(new ArtifactContentRevision("revision"), f.Physical.Request.ExpectedRevision);
        Assert.All(f.Physical.Owned!, b => Assert.Equal(0, b));
        Assert.All(f.Provider.Envelope!.Ciphertext, b => Assert.Equal(0, b));
        Assert.Same(f.Provider.Plaintext, System.Runtime.InteropServices.MemoryMarshal.TryGetArray(lease.Content, out var segment) ? segment.Array : null);
    }
    [Fact]
    public async Task MissingCapabilityRejectsBeforePhysicalRead()
    {
        var f = await Fixture();
        await Assert.ThrowsAsync<NotSupportedException>(() => new EncryptedArtifactContentStore(f.Physical, new OrdinaryProvider())
            .ReadBoundedVersionedAsync(Id, new(1000, 3)));
        Assert.Equal(0, f.Physical.Reads);
        await Assert.ThrowsAsync<NotSupportedException>(() => new EncryptedArtifactContentStore(new OrdinaryStore(), f.Provider)
            .ReadBoundedVersionedAsync(Id, new(1000, 3)));
    }
    [Fact]
    public async Task CiphertextLimitRejectsBeforeProviderAndClearsProtectedLease()
    {
        var f = await Fixture();
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Store.ReadBoundedVersionedAsync(Id, new(1000, 2)));
        Assert.Equal(0, f.Provider.Calls); Assert.All(f.Physical.Owned!, b => Assert.Equal(0, b));
    }
    [Fact]
    public async Task MalformedProtectedEnvelopeClearsSourceWithoutDecryption()
    {
        var physical = new Physical("malformed"u8.ToArray()); var provider = new Provider(Service());
        await Assert.ThrowsAnyAsync<JsonException>(() => new EncryptedArtifactContentStore(physical, provider)
            .ReadBoundedVersionedAsync(Id, new(1000, 3)));
        Assert.Equal(0, provider.Calls); Assert.All(physical.Owned!, b => Assert.Equal(0, b));
    }
    [Fact]
    public async Task AuthenticationFailureClearsSourceAndDecodedEnvelope()
    {
        var f = await Fixture();
        var envelope = JsonSerializer.Deserialize<EncryptedEnvelope>(f.Physical.Source)!;
        envelope.AuthenticationTag[0] ^= 1;
        f.Physical.Source = JsonSerializer.SerializeToUtf8Bytes(envelope);
        await Assert.ThrowsAnyAsync<CryptographicException>(() => f.Store.ReadBoundedVersionedAsync(Id, new(1000, 3)));
        Assert.All(f.Physical.Owned!, b => Assert.Equal(0, b));
        Assert.All(f.Provider.Envelope!.Ciphertext, b => Assert.Equal(0, b));
        Assert.All(f.Provider.Envelope.WrappedDataEncryptionKey, b => Assert.Equal(0, b));
    }
    [Fact]
    public async Task CancellationBeforeLeaseHandoffClearsProviderPlaintext()
    {
        var f = await Fixture(); using var cts = new CancellationTokenSource();
        f.Provider.AfterDecrypt = () => cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Store.ReadBoundedVersionedAsync(Id, new(1000, 3), cts.Token));
        Assert.All(f.Provider.Plaintext!, b => Assert.Equal(0, b));
        Assert.All(f.Physical.Owned!, b => Assert.Equal(0, b));
    }
    [Fact]
    public async Task RawDisposalFailureStillClearsPlaintextBeforeFailedHandoff()
    {
        var f = await Fixture(); f.Physical.ThrowOnDispose = true;
        await Assert.ThrowsAsync<IOException>(() => f.Store.ReadBoundedVersionedAsync(Id, new(1000, 3)));
        Assert.All(f.Provider.Plaintext!, b => Assert.Equal(0, b));
        Assert.All(f.Physical.Owned!, b => Assert.Equal(0, b));
    }
    [Fact]
    public async Task UnboundedArtifactIdentifierRejectedBeforeContextAndSourceAllocation()
    {
        var f = await Fixture();
        await Assert.ThrowsAsync<CryptographicException>(() => f.Store.ReadBoundedVersionedAsync(new(new string('a', 241)), new(1000, 3)));
        Assert.Equal(0, f.Physical.Reads);
    }
    private class OrdinaryStore : IArtifactContentStore
    {
        public Task<byte[]?> ReadAsync(ArtifactId id, CancellationToken ct = default) => throw new Exception("Ordinary fallback forbidden");
        public Task WriteAsync(ArtifactId id, ReadOnlyMemory<byte> bytes, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(ArtifactId id, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class Physical(byte[] source) : OrdinaryStore, IBoundedVersionedArtifactContentStore
    {
        public byte[] Source = source; public byte[]? Owned; public int Reads; public bool ThrowOnDispose;
        public BoundedArtifactContentReadRequest? Request;
        public long MaximumStoredRepresentationBytes => 100000;
        public Task<IArtifactContentReadLease?> ReadBoundedVersionedAsync(ArtifactId id, BoundedArtifactContentReadRequest request, CancellationToken ct = default)
        {
            Reads++; Request = request; ct.ThrowIfCancellationRequested();
            if (Source.Length > request.MaximumStoredRepresentationBytes) throw new InvalidDataException();
            Owned = (byte[])Source.Clone();
            IArtifactContentReadLease lease = new ArtifactContentReadLease(id, new("revision"), Source.Length, Owned);
            return Task.FromResult<IArtifactContentReadLease?>(ThrowOnDispose ? new ThrowingLease(lease) : lease);
        }
    }
    private sealed class ThrowingLease(IArtifactContentReadLease inner) : IArtifactContentReadLease
    {
        public ArtifactId ArtifactId => inner.ArtifactId;
        public ArtifactContentRevision Revision => inner.Revision;
        public long StoredLength => inner.StoredLength;
        public long ReturnedLength => inner.ReturnedLength;
        public ReadOnlyMemory<byte> Content => inner.Content;
        public Stream OpenReadStream() => inner.OpenReadStream();
        public void Dispose() { inner.Dispose(); throw new IOException("Injected raw disposal failure"); }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
    private class OrdinaryProvider : IEnvelopeEncryptionService
    {
        public Task<EncryptedEnvelope> EncryptAsync(ReadOnlyMemory<byte> p, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<EncryptedEnvelope> EncryptWithContextAsync(ReadOnlyMemory<byte> p, ReadOnlyMemory<byte> c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<byte[]> DecryptAsync(EncryptedEnvelope e, CancellationToken ct = default) => throw new Exception("Ordinary fallback forbidden");
        public Task<byte[]> DecryptWithContextAsync(EncryptedEnvelope e, ReadOnlyMemory<byte> c, CancellationToken ct = default) => throw new Exception("Ordinary fallback forbidden");
    }
    private sealed class Provider(DevelopmentEnvelopeEncryptionService service) : OrdinaryProvider, IBoundedEnvelopeDecryptionService
    {
        public int Calls; public EncryptedEnvelope? Envelope; public byte[]? Plaintext; public Action? AfterDecrypt;
        public async Task<byte[]> DecryptWithContextBoundedAsync(EncryptedEnvelope envelope, ReadOnlyMemory<byte> context,
            EnvelopeDecryptionLimits limits, CancellationToken ct = default)
        {
            Calls++; Envelope = envelope;
            Plaintext = await service.DecryptWithContextBoundedAsync(envelope, context, limits, ct);
            AfterDecrypt?.Invoke(); return Plaintext;
        }
    }
}
