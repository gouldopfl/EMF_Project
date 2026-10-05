using System.Buffers;
using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EMF.Core.Contracts.Storage;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Models;

namespace EMF.Extensions.VeteransClaims.Orchestration;

// Storage is supplied by trusted composition: private crash-durable staging, kept
// for the operation retention lifetime. No automatic release/GC is exposed here.
// No live metadata/content/provider dependencies exist on this retained reader.
public sealed class ProtectedReviewerRetainedMaterialStore(IArtifactContentStagingStore storage,
    IEnvelopeEncryptionService protection, int maximumBytes = 16 * 1024 * 1024) : IReviewerRetainedMaterialStore
{
    private static byte[] Context(OperationSnapshotId id) => Encoding.UTF8.GetBytes("EMF-REVIEWER-CAPTURE-v1\0" + id.Value);
    // Domain-separated storage key; this is an address, not a source revision.
    private static ArtifactContentOperationId Key(OperationSnapshotId id) => new("reviewer-" + ReviewerRetainedValidator.Hash(Context(id)));
    // Internal copies are never returned to a caller; clear their artifact buffers.
    private static void Clear(ReviewerCapturedBundle? bundle)
    {
        if (bundle?.Content is null) return;
        foreach (var bytes in bundle.Content.Values)
            if (bytes is not null) System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
    }
    // Test-only observation after ownership registration, never a production provider.
    internal Action<string, byte[]>? OwnedPlaintextCheckpoint { get; set; }
    private sealed class OwnedArtifactConverter(List<byte[]> owned, Action<string, byte[]>? checkpoint,
        CancellationToken ct) : JsonConverter<byte[]>
    {
        public override byte[] Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            ct.ThrowIfCancellationRequested();
            if (reader.TokenType != JsonTokenType.String) throw new JsonException("Invalid artifact encoding.");
            // Unescape base64 into controlled temporary bytes, never an immutable artifact string.
            var encoded = new byte[reader.HasValueSequence ? checked((int)reader.ValueSequence.Length) : reader.ValueSpan.Length];
            try
            {
                var length = reader.CopyString(encoded);
                if (length % 4 != 0) throw new JsonException("Invalid artifact base64 length.");
                var padding = length == 0 ? 0 : encoded[length - 1] == (byte)'=' ?
                    (length > 1 && encoded[length - 2] == (byte)'=' ? 2 : 1) : 0;
                var bytes = new byte[length / 4 * 3 - padding];
                owned.Add(bytes); // Register before decode, callback, cancellation or later schema reads.
                checkpoint?.Invoke("Allocated", bytes);
                ct.ThrowIfCancellationRequested();
                if (Base64.DecodeFromUtf8(encoded.AsSpan(0, length), bytes, out var consumed, out var written) != OperationStatus.Done ||
                    consumed != length || written != bytes.Length)
                    throw new JsonException("Invalid artifact base64.");
                checkpoint?.Invoke("Decoded", bytes);
                ct.ThrowIfCancellationRequested();
                return bytes;
            }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(encoded); }
        }
        public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options)
            => throw new NotSupportedException("Owned converter is decode-only.");
    }
    private ReviewerCapturedBundle DecodeOwned(ReadOnlySpan<byte> json, CancellationToken ct)
    {
        var owned = new List<byte[]>();
        HashSet<byte[]>? transferred = null;
        try
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(new OwnedArtifactConverter(owned, OwnedPlaintextCheckpoint, ct));
            var bundle = JsonSerializer.Deserialize<ReviewerCapturedBundle>(json, options)
                ?? throw new JsonException("Missing retained bundle.");
            ct.ThrowIfCancellationRequested();
            // Duplicate JSON properties may replace earlier decoded arrays. Only final content transfers.
            var resultBuffers = bundle.Content is null ? new HashSet<byte[]>() : new HashSet<byte[]>(bundle.Content.Values);
            transferred = resultBuffers;
            return bundle;
        }
        finally
        {
            foreach (var bytes in owned)
                if (transferred is null || !transferred.Contains(bytes))
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
        }
    }
    private async Task ValidateRetainedAsync(ReviewerRetainedReference reference, CancellationToken ct)
    {
        var copy = await ReadValidatedAsync(reference, ct);
        Clear(copy);
    }
    public async Task<ReviewerRetainedReference> RetainAsync(ReviewerCapturedBundle bundle, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (bundle.Manifest is null) throw new InvalidDataException("Missing capture manifest.");
        var snapshotId = bundle.Manifest.SnapshotId;
        var bytes = ReviewerRetainedValidator.Encode(bundle);
        // Freeze caller-owned mutable collections/bytes before the first await.
        ReviewerCapturedBundle? frozen = null;
        try
        {
        var hash = ReviewerRetainedValidator.Hash(bytes);
        var reference = new ReviewerRetainedReference(snapshotId,hash);
        if (bytes.Length > maximumBytes) throw new InvalidDataException("Retained bundle exceeds bound.");
        frozen = DecodeOwned(bytes, ct);
        ReviewerRetainedValidator.Validate(frozen,snapshotId,ct);
        if (await storage.ReadAsync(Key(snapshotId),ct) is not null)
        { await ValidateRetainedAsync(reference,ct); return reference; }
        var envelope = await protection.EncryptWithContextAsync(bytes, Context(snapshotId), ct);
        ct.ThrowIfCancellationRequested();
        var protectedBytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
        if (protectedBytes.Length > maximumBytes) throw new InvalidDataException("Protected bundle exceeds bound.");
        try { await storage.StageAsync(Key(snapshotId), protectedBytes, ct); }
        catch (ArtifactContentIdempotencyException)
        { await ValidateRetainedAsync(reference,ct); return reference; }
        ct.ThrowIfCancellationRequested();
        await ValidateRetainedAsync(reference,ct); return reference;
        }
        finally { Clear(frozen); System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
    }
    public async Task<ReviewerCapturedBundle> ReadValidatedAsync(ReviewerRetainedReference reference, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var bytes = await storage.ReadAsync(Key(reference.SnapshotId),ct) ?? throw new InvalidDataException("Missing retained bundle/manifest.");
        if (bytes.Length > maximumBytes) throw new InvalidDataException("Retained bundle exceeds bound.");
        try
        {
            var envelope = JsonSerializer.Deserialize<EncryptedEnvelope>(bytes) ?? throw new InvalidDataException("Missing retained envelope.");
            EncryptedEnvelopeFormat.Validate(envelope);
            if (envelope.FormatVersion != EncryptedEnvelopeFormat.ContextBoundVersion)
                throw new InvalidDataException("Retained bundle requires context-bound protection.");
            var raw = await protection.DecryptWithContextAsync(envelope,Context(reference.SnapshotId),ct);
            ReviewerCapturedBundle? bundle = null;
            var transferred = false;
            try
            {
            ct.ThrowIfCancellationRequested();
            if (raw.Length > maximumBytes || ReviewerRetainedValidator.Hash(raw) != reference.BundleSha256)
                throw new InvalidDataException("Retained reference integrity mismatch.");
            bundle = DecodeOwned(raw, ct);
            ReviewerRetainedValidator.Validate(bundle,reference.SnapshotId,ct);
            transferred = true; return bundle; // Caller owns the independent decoded buffers.
            }
            finally { if (!transferred) Clear(bundle); System.Security.Cryptography.CryptographicOperations.ZeroMemory(raw); }
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or System.Security.Cryptography.CryptographicException)
        { throw new InvalidDataException("Invalid protected retained bundle.",ex); }
    }
    public async Task<ReviewerRetainedReference> CaptureAndRetainAsync(IReviewerCaptureSession capture, CancellationToken ct = default)
    {
        ReviewerCapturedBundle? bundle = null;
        try
        {
            await using (capture) { bundle = await capture.CaptureAsync(ct); }
            return await RetainAsync(bundle,ct); // Live resources are closed first.
        }
        finally
        {
            // Only this helper's capture result is owned here. RetainAsync itself
            // leaves caller-owned bundles intact and publishes independent ciphertext.
            Clear(bundle);
        }
    }
}
