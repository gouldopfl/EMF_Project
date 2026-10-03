using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EMF.Core.Contracts.Storage;
using EMF.Security.Encryption.Envelope.Models;
using EMF.Core.Models.Identities;
using EMF.Security.Encryption.Envelope;

namespace EMF.Security.Storage;

public sealed class EncryptedArtifactContentStore :
    IVersionedArtifactContentStore
{
    private readonly IArtifactContentStore _inner;
    private readonly IEnvelopeEncryptionService _encryption;

    public EncryptedArtifactContentStore(
        IArtifactContentStore inner,
        IEnvelopeEncryptionService encryption)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(encryption);

        _inner = inner;
        _encryption = encryption;
    }

    public Task WriteAsync(
        ArtifactId artifactId,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        return WriteEncryptedAsync(
            artifactId,
            content,
            cancellationToken);
    }

    private async Task WriteEncryptedAsync(
        ArtifactId artifactId,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        var envelope =
            await _encryption.EncryptWithContextAsync(
                content,
                ArtifactEnvelopeContext.Create(artifactId),
                cancellationToken);

        ValidateArtifactEnvelope(envelope);

        var serialized =
            JsonSerializer.SerializeToUtf8Bytes(envelope);

        await _inner.WriteAsync(
            artifactId,
            serialized,
            cancellationToken);
    }

    public Task<byte[]?> ReadAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default)
    {
        return ReadEncryptedAsync(
            artifactId,
            cancellationToken);
    }

    public Task DeleteAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default)
    {
        return _inner.DeleteAsync(
            artifactId,
            cancellationToken);
    }

    private async Task<byte[]?> ReadEncryptedAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken)
    {
        var serialized =
            await _inner.ReadAsync(
                artifactId,
                cancellationToken);

        if (serialized is null)
            return null;

        var envelope =
            JsonSerializer.Deserialize<EncryptedEnvelope>(
                serialized);

        if (envelope is null)
            throw new InvalidOperationException(
                "Encrypted artifact envelope is invalid.");

        ValidateArtifactEnvelope(envelope);

        return await _encryption.DecryptWithContextAsync(
            envelope,
            ArtifactEnvelopeContext.Create(artifactId),
            cancellationToken);
    }

    private IVersionedArtifactContentStore Versioned => _inner as IVersionedArtifactContentStore
        ?? throw new NotSupportedException("Underlying content store does not support conditional mutation.");

    public async Task<ArtifactContentSnapshot?> ReadVersionedAsync(ArtifactId id, CancellationToken cancellationToken = default)
    {
        var snapshot = await Versioned.ReadVersionedAsync(id, cancellationToken);
        if (snapshot is null) return null;
        var envelope = JsonSerializer.Deserialize<EncryptedEnvelope>(snapshot.Content)
            ?? throw new CryptographicException("Encrypted artifact envelope is invalid.");
        ValidateArtifactEnvelope(envelope);
        return new(await _encryption.DecryptWithContextAsync(envelope, ArtifactEnvelopeContext.Create(id), cancellationToken), snapshot.Revision);
    }
    public Task<ArtifactContentMutationReceipt?> GetMutationOutcomeAsync(ArtifactContentOperationId operationId,
        CancellationToken cancellationToken = default) => Versioned.GetMutationOutcomeAsync(operationId, cancellationToken);
    public Task<IReadOnlyList<ArtifactContentAuditObligation>> ReadAuditObligationsAsync(
        ArtifactContentReceiptCursor? afterCursor, int limit, CancellationToken cancellationToken = default)
        => Versioned.ReadAuditObligationsAsync(afterCursor, limit, cancellationToken);
    public Task<ArtifactContentMutationResult> DeleteIfRevisionMatchesAsync(ArtifactId id, ArtifactContentRevision expected,
        ArtifactContentMutationContext context, CancellationToken cancellationToken = default)
        => Versioned.DeleteIfRevisionMatchesAsync(id, expected, context, cancellationToken);

    public async Task<ArtifactContentMutationResult> CreateIfAbsentAsync(ArtifactId id, ReadOnlyMemory<byte> content,
        ArtifactContentMutationContext context, CancellationToken cancellationToken = default)
        => await (await PrepareCreateAsync(id, content, context, cancellationToken)).ExecuteAsync(cancellationToken);
    public async Task<ArtifactContentMutationResult> ReplaceIfRevisionMatchesAsync(ArtifactId id, ArtifactContentRevision expected,
        ReadOnlyMemory<byte> content, ArtifactContentMutationContext context, CancellationToken cancellationToken = default)
        => await (await PrepareReplaceAsync(id, expected, content, context, cancellationToken)).ExecuteAsync(cancellationToken);

    // Retain exactly this encrypted candidate for same-operation retries. This
    // in-memory capability is NOT a durable ADR-048/049 staging coordinator.
    public Task<PreparedEncryptedArtifactMutation> PrepareCreateAsync(ArtifactId id, ReadOnlyMemory<byte> content,
        ArtifactContentMutationContext context, CancellationToken cancellationToken = default)
        => PrepareAsync(id, null, content, context, cancellationToken);
    public Task<PreparedEncryptedArtifactMutation> PrepareReplaceAsync(ArtifactId id, ArtifactContentRevision expected,
        ReadOnlyMemory<byte> content, ArtifactContentMutationContext context, CancellationToken cancellationToken = default)
        => PrepareAsync(id, expected, content, context, cancellationToken);
    private async Task<PreparedEncryptedArtifactMutation> PrepareAsync(ArtifactId id, ArtifactContentRevision? expected,
        ReadOnlyMemory<byte> content, ArtifactContentMutationContext context, CancellationToken ct)
    {
        var underlying = Versioned; // no unconditional fallback
        ArgumentNullException.ThrowIfNull(context);
        if (await underlying.GetMutationOutcomeAsync(context.OperationId, ct) is not null)
            throw new ArtifactContentIdempotencyException(); // cannot reconstruct randomized physical request
        var envelope = await _encryption.EncryptWithContextAsync(content, ArtifactEnvelopeContext.Create(id), ct);
        ValidateArtifactEnvelope(envelope);
        return new(underlying, id, expected, JsonSerializer.SerializeToUtf8Bytes(envelope), context);
    }

    private static void ValidateArtifactEnvelope(EncryptedEnvelope envelope)
    {
        EncryptedEnvelopeFormat.Validate(envelope);

        // Generic decryption supports legacy migration, but an artifact read
        // must never accept ciphertext without authenticated artifact identity.
        if (envelope.FormatVersion != EncryptedEnvelopeFormat.ContextBoundVersion)
        {
            throw new CryptographicException(
                "Artifact content requires an identity-bound encrypted envelope.");
        }
    }


}
