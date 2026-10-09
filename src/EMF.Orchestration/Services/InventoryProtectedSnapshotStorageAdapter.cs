using System.Security.Cryptography;
using System.Text.Json;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Inventory.Contracts;
using EMF.Inventory.Models;
using EMF.Inventory.Storage;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Models;
using EMF.Security.Storage;

namespace EMF.Orchestration.Services;

// The host injects an isolated retained namespace and its existing protection service.
// It is never the canonical child content namespace or a substitute ingestion authority.
public sealed class InventoryProtectedSnapshotStorageAdapter : IInventoryProtectedSnapshotStorage
{
    private readonly IPreparedArtifactContentStore _physical;
    private readonly IEnvelopeEncryptionService _encryption;
    private readonly InventoryProcessingLimits _limits;
    private readonly SemaphoreSlim _buffers = new(1, 1);
    public InventoryProtectedSnapshotStorageAdapter(IPreparedArtifactContentStore physical, IEnvelopeEncryptionService encryption, InventoryProcessingLimits? limits = null)
    { (_physical, _encryption) = (physical, encryption); _limits = limits ?? new(); _limits.Validate(); }
    private static ArtifactId Id(string id) => new("inventory-retained-" + InventoryIdentity.Validate(id));
    private static ArtifactContentMutationContext Context(string operation, string owner) => new(new(operation), new ArtifactContentOwnershipToken(owner));
    private void Bound(InventoryRetainedBinding b)
    {
        if (b.Representation != "inventory-sqlite-v1" || b.Length <= 0 || b.Length > _limits.MaximumSnapshotBytes || b.Length > _limits.MaximumPlaintextBytes || b.Fingerprint.Length != 64) throw new InvalidDataException("Invalid retained representation/length.");
        foreach (var id in new[] { b.ObjectId, b.OwnerToken, b.Revision, b.CreateOperationId, b.ReleaseOperationId }) InventoryIdentity.Validate(id);
    }
    private static void Receipt(ArtifactContentMutationReceipt r, InventoryRetainedBinding b, bool delete)
    {
        if (r.ArtifactId != Id(b.ObjectId) || r.OperationId.Value != (delete ? b.ReleaseOperationId : b.CreateOperationId) || r.OwnershipToken?.Value != b.OwnerToken ||
            r.Kind != (delete ? ArtifactContentMutationKind.Delete : ArtifactContentMutationKind.Create) ||
            r.Outcome != (delete ? ArtifactContentMutationOutcome.Deleted : ArtifactContentMutationOutcome.Created) ||
            (delete ? r.PriorRevision?.Value : r.CurrentRevision?.Value) != b.Revision) throw new InvalidDataException("Retained receipt binding mismatch.");
    }
    public async Task<InventoryRetainedBinding> SealAsync(InventoryRetentionRecord request, string plaintextPath, CancellationToken ct = default)
    {
        if (request.Status != InventoryRetentionStatus.Sealing || request.Fingerprint is null || request.Length <= 0 || request.Length > _limits.MaximumSnapshotBytes || request.Length > _limits.MaximumPlaintextBytes) throw new InvalidDataException("Snapshot protection admission failed.");
        await _buffers.WaitAsync(ct).ConfigureAwait(false);
        byte[]? plain = null, serialized = null; EncryptedEnvelope? envelope = null;
        try
        {
            var known = await _physical.GetMutationOutcomeAsync(new(request.CreateOperationId), ct);
            if (known is not null)
            {
                var recovered = new InventoryRetainedBinding(request.ObjectId, "inventory-sqlite-v1", known.CurrentRevision?.Value ?? throw new InvalidDataException(), request.Fingerprint, request.Length, request.OwnerToken, request.CreateOperationId, request.ReleaseOperationId);
                Bound(recovered); Receipt(known, recovered, false); plain = await ReadPlainAsync(recovered, ct); return recovered;
            }
            LinuxInventorySnapshotWorkspace.Validate(plaintextPath, false);
            await using (var stream = File.OpenRead(plaintextPath))
            {
                if (stream.Length != request.Length) throw new InvalidDataException("Snapshot length changed.");
                plain = new byte[checked((int)request.Length)]; await stream.ReadExactlyAsync(plain, ct);
                if (stream.Position != stream.Length) throw new InvalidDataException("Snapshot grew.");
            }
            if (Convert.ToHexString(SHA256.HashData(plain)) != request.Fingerprint) throw new InvalidDataException("Snapshot fingerprint changed.");
            envelope = await _encryption.EncryptWithContextAsync(plain, ArtifactEnvelopeContext.Create(Id(request.ObjectId)), ct);
            ValidateEnvelope(envelope, request.Length);
            using (var bounded = new BoundedBufferStream(checked((int)_limits.MaximumProtectedBytes)))
            { JsonSerializer.Serialize(bounded, envelope); serialized = bounded.ToArray(); }
            await using var prepared = await _physical.PreparePhysicalCreateAsync(Id(request.ObjectId), serialized, Context(request.CreateOperationId, request.OwnerToken), ct);
            ArtifactContentMutationReceipt created;
            try { created = (await prepared.ExecuteAsync(ct)).Receipt; }
            catch (Exception error) when (error is not OperationCanceledException)
            { var recovered = await _physical.GetMutationOutcomeAsync(new(request.CreateOperationId), ct); if (recovered is null) throw; created = recovered; }
            var binding = new InventoryRetainedBinding(request.ObjectId, "inventory-sqlite-v1", created.CurrentRevision?.Value ?? throw new InvalidDataException(), request.Fingerprint, request.Length, request.OwnerToken, request.CreateOperationId, request.ReleaseOperationId);
            Receipt(created, binding, false); return binding;
        }
        finally { if (plain is not null) CryptographicOperations.ZeroMemory(plain); if (serialized is not null) CryptographicOperations.ZeroMemory(serialized); if (envelope is not null) CryptographicOperations.ZeroMemory(envelope.Ciphertext); _buffers.Release(); }
    }
    private void ValidateEnvelope(EncryptedEnvelope envelope, long length)
    {
        EncryptedEnvelopeFormat.Validate(envelope);
        if (envelope.FormatVersion != EncryptedEnvelopeFormat.ContextBoundVersion || envelope.Ciphertext.LongLength != length || length > _limits.MaximumPlaintextBytes || envelope.KeyEncryptionKeyId.Length > 4096 || envelope.WrappedDataEncryptionKey.Length > 1024)
            throw new InvalidDataException("Retained encrypted representation limit.");
    }
    private async Task<byte[]> ReadPlainAsync(InventoryRetainedBinding b, CancellationToken ct)
    {
        Bound(b);
        var created = await _physical.GetMutationOutcomeAsync(new(b.CreateOperationId), ct) ?? throw new InvalidDataException("Missing retained create receipt."); Receipt(created, b, false);
        var snapshot = await _physical.ReadVersionedAsync(Id(b.ObjectId), ct) ?? throw new InvalidDataException("Missing retained snapshot.");
        byte[]? plain = null; EncryptedEnvelope? envelope = null;
        try
        {
            if (snapshot.Revision.Value != b.Revision || snapshot.Content.LongLength > _limits.MaximumProtectedBytes) throw new InvalidDataException("Wrong retained revision/size.");
            // Limit JSON scalars before decoding base64 into another full array.
            var reader = new Utf8JsonReader(snapshot.Content, new JsonReaderOptions { MaxDepth = 4 });
            string? property = null; var seen = new HashSet<string>(StringComparer.Ordinal);
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    if (reader.ValueSpan.Length > 64) throw new InvalidDataException("Envelope property name limit.");
                    property = reader.GetString();
                    if (property is not (nameof(EncryptedEnvelope.FormatVersion) or nameof(EncryptedEnvelope.Ciphertext) or nameof(EncryptedEnvelope.Nonce) or nameof(EncryptedEnvelope.AuthenticationTag) or nameof(EncryptedEnvelope.WrappedDataEncryptionKey) or nameof(EncryptedEnvelope.KeyEncryptionKeyId) or nameof(EncryptedEnvelope.Algorithm)) || !seen.Add(property)) throw new InvalidDataException("Unknown/duplicate envelope field.");
                }
                else if (reader.TokenType == JsonTokenType.String)
                {
                    long maximum = property == nameof(EncryptedEnvelope.Ciphertext) ? checked(((b.Length + 2) / 3) * 4) : property == nameof(EncryptedEnvelope.WrappedDataEncryptionKey) ? 1368 : property == nameof(EncryptedEnvelope.KeyEncryptionKeyId) ? 24576 : 128;
                    if (reader.ValueSpan.Length > maximum) throw new InvalidDataException("Encrypted representation field exceeds admission limit.");
                }
            }
            envelope = JsonSerializer.Deserialize<EncryptedEnvelope>(snapshot.Content) ?? throw new InvalidDataException(); ValidateEnvelope(envelope, b.Length);
            plain = await _encryption.DecryptWithContextAsync(envelope, ArtifactEnvelopeContext.Create(Id(b.ObjectId)), ct);
            if (plain.LongLength != b.Length || Convert.ToHexString(SHA256.HashData(plain)) != b.Fingerprint) throw new InvalidDataException("Wrong retained length/fingerprint.");
            var result = plain; plain = null; return result;
        }
        finally { if (plain is not null) CryptographicOperations.ZeroMemory(plain); if (envelope is not null) CryptographicOperations.ZeroMemory(envelope.Ciphertext); CryptographicOperations.ZeroMemory(snapshot.Content); }
    }
    public async Task MaterializeAsync(InventoryRetainedBinding binding, string destination, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        await _buffers.WaitAsync(ct).ConfigureAwait(false); byte[]? plain = null;
        try
        {
            plain = await ReadPlainAsync(binding, ct).ConfigureAwait(false);
            await using (var file = new FileStream(destination, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite }))
            { await file.WriteAsync(plain, ct); file.Flush(true); }
            LinuxInventorySnapshotWorkspace.FlushDirectory(Path.GetDirectoryName(destination)!);
        }
        finally { if (plain is not null) CryptographicOperations.ZeroMemory(plain); _buffers.Release(); }
    }
    public async Task ReleaseAsync(InventoryRetainedBinding binding, CancellationToken ct = default)
    {
        Bound(binding);
        var known = await _physical.GetMutationOutcomeAsync(new(binding.ReleaseOperationId), ct);
        if (known is not null) { Receipt(known, binding, true); return; }
        var creation = await _physical.GetMutationOutcomeAsync(new(binding.CreateOperationId), ct) ?? throw new InvalidDataException(); Receipt(creation, binding, false);
        await using var prepared = await _physical.PreparePhysicalDeleteAsync(Id(binding.ObjectId), new(binding.Revision), Context(binding.ReleaseOperationId, binding.OwnerToken), ct);
        var result = await prepared.ExecuteAsync(ct); Receipt(result.Receipt, binding, true);
    }
    private sealed class BoundedBufferStream(int limit) : MemoryStream
    {
        private void Admit(int count) { if (checked(Position + count) > limit) throw new InvalidDataException("Protected representation exceeds parent limit."); }
        public override void Write(byte[] buffer, int offset, int count) { Admit(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Admit(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value) { Admit(1); base.WriteByte(value); }
    }
}
