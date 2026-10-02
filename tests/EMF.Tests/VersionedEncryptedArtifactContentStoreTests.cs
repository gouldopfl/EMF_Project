using System.Text;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Persistence.Storage;
using EMF.Security.Encryption.Envelope.Services;
using EMF.Security.Encryption.Models;
using EMF.Security.Encryption.Services;
using EMF.Security.Storage;

namespace EMF.Tests;

public sealed class VersionedEncryptedArtifactContentStoreTests
{
    [Fact]
    public async Task PreparedCiphertextRetryPreservesReceiptAndPhysicalRevisionWithoutPlaintextMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "emf-encrypted-versioned-" + Guid.NewGuid());
        try
        {
            var physical = new FileSystemArtifactContentStore(root);
            var encrypted = Create(physical, out var encryption); var id = new ArtifactId("synthetic");
            var context = new ArtifactContentMutationContext(ArtifactContentOperationId.New());
            var plaintext = Encoding.UTF8.GetBytes("synthetic-protected-value-" + Guid.NewGuid());
            var prepared = await encrypted.PrepareCreateAsync(id, plaintext, context);
            var result = await prepared.ExecuteAsync();
            var committedBytes = (await physical.ReadVersionedAsync(id))!.Content;
            Assert.Equal(result.Receipt, (await prepared.ExecuteAsync()).Receipt);
            Assert.Equal(committedBytes, (await physical.ReadVersionedAsync(id))!.Content);
            Assert.Equal(1, encryption.Encryptions);
            var raw = await physical.ReadVersionedAsync(id); var snapshot = await encrypted.ReadVersionedAsync(id);
            Assert.Equal(raw!.Revision, snapshot!.Revision); Assert.Equal(plaintext, snapshot.Content);
            Assert.Equal(result.Receipt, await encrypted.GetMutationOutcomeAsync(context.OperationId));
            Assert.DoesNotContain(Encoding.UTF8.GetString(plaintext), Encoding.UTF8.GetString(raw.Content));
            Assert.DoesNotContain(Encoding.UTF8.GetString(plaintext), Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(root, ".content-catalog.sqlite"))));
            await Assert.ThrowsAsync<ArtifactContentIdempotencyException>(() => encrypted.CreateIfAbsentAsync(id, plaintext, context));
            await encrypted.WriteAsync(id, new byte[] { 2 });
            Assert.Equal(result.Receipt, (await prepared.ExecuteAsync()).Receipt);
            Assert.Equal(new byte[] { 2 }, (await encrypted.ReadVersionedAsync(id))!.Content);
            var stale = await encrypted.ReplaceIfRevisionMatchesAsync(id, raw.Revision, new byte[] { 3 }, new(ArtifactContentOperationId.New()));
            Assert.Equal(ArtifactContentMutationOutcome.VersionConflict, stale.Outcome);
            var current = (await encrypted.ReadVersionedAsync(id))!.Revision;
            var replacement = await encrypted.PrepareReplaceAsync(id, current, new byte[] { 4 }, new(ArtifactContentOperationId.New()));
            var replaced = await replacement.ExecuteAsync();
            Assert.Equal(replaced.Receipt, (await replacement.ExecuteAsync()).Receipt);
            Assert.Equal(new byte[] { 4 }, (await encrypted.ReadVersionedAsync(id))!.Content);
            var deleted = await encrypted.DeleteIfRevisionMatchesAsync(id, replaced.CurrentRevision!.Value, new(ArtifactContentOperationId.New()));
            Assert.Equal(ArtifactContentMutationOutcome.Deleted, deleted.Outcome);
            Assert.Null(await encrypted.ReadVersionedAsync(id));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task UnsupportedVersionedUnderlyingStoreNeverFallsBackToLegacyCalls()
    {
        var store = Create(new NoVersionedStore());
        await Assert.ThrowsAsync<NotSupportedException>(() => store.CreateIfAbsentAsync(new("synthetic"), new byte[] { 1 }, new(ArtifactContentOperationId.New())));
        await Assert.ThrowsAsync<NotSupportedException>(() => store.ReadVersionedAsync(new("synthetic")));
    }

    private static EncryptedArtifactContentStore Create(IArtifactContentStore physical) => Create(physical, out _);
    private static EncryptedArtifactContentStore Create(IArtifactContentStore physical, out CountingEncryption encryption)
    {
        encryption = new(new DevelopmentEnvelopeEncryptionService(new InMemoryEncryptionKeyProvider(new[]
        { new EncryptionKey { KeyId = "synthetic-test-key", KeyMaterial = new byte[32] } })));
        return new(physical, encryption);
    }
    private sealed class CountingEncryption(EMF.Security.Encryption.Envelope.IEnvelopeEncryptionService inner)
        : EMF.Security.Encryption.Envelope.IEnvelopeEncryptionService
    {
        public int Encryptions { get; private set; }
        public Task<EMF.Security.Encryption.Envelope.Models.EncryptedEnvelope> EncryptWithContextAsync(ReadOnlyMemory<byte> plaintext,
            ReadOnlyMemory<byte> context, CancellationToken ct = default)
        { Encryptions++; return inner.EncryptWithContextAsync(plaintext, context, ct); }
        public Task<EMF.Security.Encryption.Envelope.Models.EncryptedEnvelope> EncryptAsync(ReadOnlyMemory<byte> plaintext, CancellationToken ct = default)
            => inner.EncryptAsync(plaintext, ct);
        public Task<byte[]> DecryptAsync(EMF.Security.Encryption.Envelope.Models.EncryptedEnvelope envelope, CancellationToken ct = default)
            => inner.DecryptAsync(envelope, ct);
        public Task<byte[]> DecryptWithContextAsync(EMF.Security.Encryption.Envelope.Models.EncryptedEnvelope envelope,
            ReadOnlyMemory<byte> context, CancellationToken ct = default) => inner.DecryptWithContextAsync(envelope, context, ct);
    }
    private sealed class NoVersionedStore : IArtifactContentStore
    {
        public Task WriteAsync(ArtifactId id, ReadOnlyMemory<byte> bytes, CancellationToken ct = default) => throw new Exception("Unsafe fallback");
        public Task<byte[]?> ReadAsync(ArtifactId id, CancellationToken ct = default) => throw new Exception("Unsafe fallback");
        public Task DeleteAsync(ArtifactId id, CancellationToken ct = default) => throw new Exception("Unsafe fallback");
    }
}
