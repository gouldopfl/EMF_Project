using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;

namespace EMF.Security.Storage;

// Cryptographic bytes remain private. Reusing this capability preserves the
// exact physical canonical request; it contains no plaintext or raw DEK.
public sealed class PreparedEncryptedArtifactMutation
{
    private readonly IVersionedArtifactContentStore _store;
    private readonly ArtifactId _id;
    private readonly ArtifactContentRevision? _expected;
    private readonly byte[] _encryptedContent;
    private readonly ArtifactContentMutationContext _context;
    internal PreparedEncryptedArtifactMutation(IVersionedArtifactContentStore store, ArtifactId id,
        ArtifactContentRevision? expected, byte[] encryptedContent, ArtifactContentMutationContext context)
        => (_store, _id, _expected, _encryptedContent, _context) = (store, id, expected, encryptedContent, context);
    public Task<ArtifactContentMutationResult> ExecuteAsync(CancellationToken cancellationToken = default)
        => _expected is { } revision
            ? _store.ReplaceIfRevisionMatchesAsync(_id, revision, _encryptedContent, _context, cancellationToken)
            : _store.CreateIfAbsentAsync(_id, _encryptedContent, _context, cancellationToken);
}
