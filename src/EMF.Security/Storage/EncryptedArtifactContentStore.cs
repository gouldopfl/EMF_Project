using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EMF.Core.Contracts.Storage;
using EMF.Security.Encryption.Envelope.Models;
using EMF.Core.Models.Identities;
using EMF.Security.Encryption.Envelope;

namespace EMF.Security.Storage;

public sealed class EncryptedArtifactContentStore :
    IArtifactContentStore
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
                GetContext(artifactId),
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
            GetContext(artifactId),
            cancellationToken);
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

    private static byte[] GetContext(
        ArtifactId artifactId) =>
        Encoding.UTF8.GetBytes(
            $"EMF-ARTIFACT-ID\0{artifactId.Value}");

}
