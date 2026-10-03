using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Security.Auditing;
using EMF.Security.Auditing.Models;
using EMF.Security.Authorization;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Models;
using EMF.Security.Models;
using EMF.Security.Models.Identities;
using EMF.Security.Storage;

namespace EMF.Tests;

public sealed partial class ArtifactEnvelopeRewrappingServiceTests
{
    // These existing unit tests isolate lifecycle failure handling. Durable Linux
    // staging, real receipt replay and real cryptography are covered separately.
    private static ArtifactEnvelopeRewrappingService CreateService(IArtifactContentStore store,
        IEnvelopeKeyRewrappingService provider, IAuthorizationPolicy policy, RecordingSecurityAuditSink audit)
        => new(store as IVersionedArtifactContentStore ?? new VersionedTestStore(store),
            new TestAuthenticatedProvider(provider), policy, audit, new RewrapTestAuthority(), new RewrapTestJournal(), new RewrapTestStaging());
    private sealed class TestAuthenticatedProvider(IEnvelopeKeyRewrappingService inner) : IAuthenticatedEnvelopeKeyRewrappingService
    {
        public Task<EncryptedEnvelope> RewrapAuthenticatedAsync(EncryptedEnvelope envelope, ReadOnlyMemory<byte> context, CancellationToken cancellationToken = default)
            => inner.RewrapAsync(envelope, cancellationToken);
    }
    private sealed class VersionedTestStore(IArtifactContentStore inner) : IVersionedArtifactContentStore
    {
        public Task<byte[]?> ReadAsync(ArtifactId id, CancellationToken ct = default) => inner.ReadAsync(id, ct);
        public Task WriteAsync(ArtifactId id, ReadOnlyMemory<byte> bytes, CancellationToken ct = default) => inner.WriteAsync(id, bytes, ct);
        public Task DeleteAsync(ArtifactId id, CancellationToken ct = default) => inner.DeleteAsync(id, ct);
        public async Task<ArtifactContentSnapshot?> ReadVersionedAsync(ArtifactId id, CancellationToken ct = default)
        { var bytes = await inner.ReadAsync(id, ct); return bytes is null ? null : new(bytes, new("fixture-revision")); }
        public async Task<ArtifactContentMutationResult> ReplaceIfRevisionMatchesAsync(ArtifactId id, ArtifactContentRevision expected, ReadOnlyMemory<byte> bytes, ArtifactContentMutationContext context, CancellationToken ct = default)
        { await inner.WriteAsync(id, bytes, ct); throw new InvalidOperationException("Fixture must not silently perform a successful mutation."); }
        public Task<ArtifactContentMutationResult> CreateIfAbsentAsync(ArtifactId id, ReadOnlyMemory<byte> bytes, ArtifactContentMutationContext context, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ArtifactContentMutationResult> DeleteIfRevisionMatchesAsync(ArtifactId id, ArtifactContentRevision expected, ArtifactContentMutationContext context, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ArtifactContentMutationReceipt?> GetMutationOutcomeAsync(ArtifactContentOperationId id, CancellationToken ct = default) => Task.FromResult<ArtifactContentMutationReceipt?>(null);
        public Task<IReadOnlyList<ArtifactContentAuditObligation>> ReadAuditObligationsAsync(ArtifactContentReceiptCursor? after, int limit, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ArtifactContentAuditObligation>>([]);
    }
}
internal sealed class RewrapTestAuthority : IArtifactMutationAuthority
{
    internal bool Adopted { get; set; } = true;
    internal string Classification { get; set; } = ProtectionClassifications.Confidential;
    internal string Revision { get; set; } = "classification-1";
    public Task<IArtifactMutationAuthorityLease> AcquireAsync(ArtifactId id, CancellationToken cancellationToken = default)
        => Task.FromResult<IArtifactMutationAuthorityLease>(new Lease(id, new(Classification), new(Revision), Adopted));
    private sealed record Lease(ArtifactId ArtifactId, ProtectionClassificationId ClassificationId,
        ArtifactClassificationRevision ClassificationRevision, bool IsAdopted) : IArtifactMutationAuthorityLease
    { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
}
internal sealed class RewrapTestStaging : IArtifactContentStagingStore
{
    private readonly Dictionary<ArtifactContentOperationId, byte[]> _candidates = [];
    public Task StageAsync(ArtifactContentOperationId id, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    { if (_candidates.TryGetValue(id, out var previous) && !previous.AsSpan().SequenceEqual(bytes.Span)) throw new ArtifactContentIdempotencyException(); _candidates[id] = bytes.ToArray(); return Task.CompletedTask; }
    public Task<byte[]?> ReadAsync(ArtifactContentOperationId id, CancellationToken cancellationToken = default) => Task.FromResult(_candidates.TryGetValue(id, out var bytes) ? bytes.ToArray() : null);
}
internal sealed class RewrapTestJournal : IArtifactRewrapJournal
{
    private readonly Dictionary<SecurityMutationOperationId, ArtifactRewrapIntent> _intents = [];
    private readonly Dictionary<SecurityMutationOperationId, ArtifactRewrapRecoveryWork> _review = [];
    public Task<ArtifactRewrapIntent?> ReadAsync(SecurityMutationOperationId id, CancellationToken cancellationToken = default) => Task.FromResult(_intents.GetValueOrDefault(id));
    public Task CreateAsync(ArtifactRewrapIntent intent, CancellationToken cancellationToken = default) { _intents.Add(intent.OperationId, intent); return Task.CompletedTask; }
    public Task UpdateAsync(ArtifactRewrapIntent expected, ArtifactRewrapIntent updated, CancellationToken cancellationToken = default)
    { if (_intents[expected.OperationId] != expected) throw new InvalidOperationException("Fixture transition conflict."); _intents[expected.OperationId] = updated; return Task.CompletedTask; }
    public Task<IReadOnlyList<ArtifactRewrapIntent>> ReadPendingAsync(int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ArtifactRewrapIntent>>(_intents.Values.Where(x => x.State != ArtifactRewrapState.Completed).Take(limit).ToArray());
    public Task<ArtifactRewrapRecoveryWork?> ReadReviewAsync(SecurityMutationOperationId id, CancellationToken ct = default) => Task.FromResult(_review.GetValueOrDefault(id));
    public Task RecordReviewAsync(ArtifactRewrapRecoveryWork work, CancellationToken cancellationToken = default) { _review[work.OperationId] = work; return Task.CompletedTask; }
    public Task<IReadOnlyList<ArtifactRewrapRecoveryWork>> ReadRecoveryWorkAsync(int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ArtifactRewrapRecoveryWork>>(_review.Values.Take(limit).ToArray());
}
