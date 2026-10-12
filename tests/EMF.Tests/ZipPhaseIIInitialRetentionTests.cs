using System.Security.Cryptography;
using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts.Zip;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

[Collection(ZipProcessWideGateCollection.Name)]
public sealed class ZipPhaseIIInitialRetentionTests
{
    private sealed class Fixture : IAsyncDisposable
    {
        internal ArtifactIngestionFixture Evidence = null!;
        internal ZipDurableRuntimeFixture Runtime = null!;
        internal ZipAdmissionFixture.Clock Clock { get; } = new();
        internal ZipDurableProfile Profile = null!;
        internal ZipParentAdmission Approved = null!;
        internal ZipAuthorizedParentRetentionService Service => new(Runtime.Journal, Runtime.ParentRetention, Profile, Clock);
        internal static async Task<Fixture> CreateAsync(bool wrongNamespace = false)
        {
            var f = new Fixture { Evidence = await ArtifactIngestionFixture.CreateAsync() };
            try
            {
                f.Runtime = new(f.Evidence, f.Clock); await f.Runtime.Journal.InitializeAsync();
                f.Profile = ZipDurableProfile.Create(wrongNamespace ? new string('A', 64) : f.Runtime.ParentStorage.NamespaceId,
                    f.Runtime.ChildStorage.NamespaceId, new("clamd", "test-policy", new string('C', 64)), "synthetic-test-protection");
                var binding = new ZipAdmissionBinding("workflow", "workflow-operation", "zip-archives", "parent",
                    "source-parent", "source-revision", "synthetic-original-actor", "synthetic-authorized-operation",
                    f.Profile.CanonicalJson, f.Profile.Fingerprint, f.Profile.ParentNamespaceId, f.Profile.ChildNamespaceId);
                var p = await f.Runtime.Journal.ResolveOrReserveAdmissionAsync(new("synthetic-test-issuer", "retention-request"), binding);
                var approval = new ZipAdmissionApproval(p.OperationId, ZipAdmissionValidation.BindingHash(binding), p.Key.IssuerId,
                    "synthetic-decision", "authority-revision", "policy-v1", ZipAuthorityCapabilities.Admit | ZipAuthorityCapabilities.Recover,
                    f.Clock.Now, f.Clock.Now.AddHours(1));
                f.Approved = await f.Runtime.Journal.AppendAdmissionEventAsync(p,
                    new("approval", ZipAdmissionEventKind.Approved, binding.OriginalActorId, f.Clock.Now, approval));
                return f;
            }
            catch { await f.DisposeAsync(); throw; }
        }
        public ValueTask DisposeAsync() => Evidence.DisposeAsync();
    }

    private sealed class SourceStore : IZipGuardedBoundedArtifactContentStore
    {
        internal int Reads;
        internal ArtifactId? RequestedArtifact;
        internal BoundedArtifactContentReadRequest? RequestedBounds;
        internal byte[]? Plaintext;
        internal string ReturnedRevision = "source-revision";
        internal Func<Task>? BeforeRead;
        internal Func<Task>? BeforeDisposal;
        public async Task<IArtifactContentReadLease?> ReadBoundedVersionedGuardedAsync(ArtifactId artifactId,
            BoundedArtifactContentReadRequest request, IZipEffectBoundaryGuard guard, CancellationToken ct = default)
        {
            RequestedArtifact = artifactId; RequestedBounds = request;
            if (BeforeRead is not null) await BeforeRead();
            await guard.RevalidateAsync(ct);
            using (guard.Accept(ct))
            {
                Reads++; Plaintext = ZipDurableRuntimeFixture.Zip();
                Assert.True(Plaintext.Length <= request.MaximumReturnedContentBytes);
                return new SourceLease(new(artifactId, new(ReturnedRevision), Plaintext.Length, Plaintext), this);
            }
        }
        private sealed class SourceLease(ArtifactContentReadLease inner, SourceStore owner) : IArtifactContentReadLease
        {
            public ArtifactId ArtifactId => inner.ArtifactId;
            public ArtifactContentRevision Revision => inner.Revision;
            public long StoredLength => inner.StoredLength;
            public long ReturnedLength => inner.ReturnedLength;
            public ReadOnlyMemory<byte> Content => inner.Content;
            public Stream OpenReadStream() => inner.OpenReadStream();
            public void Dispose() => inner.Dispose();
            public async ValueTask DisposeAsync()
            {
                try { if (owner.BeforeDisposal is not null) await owner.BeforeDisposal(); }
                finally { inner.Dispose(); }
            }
        }
    }

    [Fact]
    public async Task Authorized_exact_source_produces_ParentBound_without_ownership_or_child_work()
    {
        await using var f = await Fixture.CreateAsync();
        await using var grant = new SyntheticZipParentRetentionGrant(f.Approved, f.Clock);
        var source = new SourceStore();
        var result = await f.Service.RetainAsync(grant, source);
        Assert.Equal(ZipAdmissionState.ParentBound, result.State);
        Assert.Equal(f.Approved.Binding, result.Binding); Assert.Equal(f.Approved.Approval, result.Approval);
        Assert.Equal(f.Approved.Binding.SourceContentId, source.RequestedArtifact!.Value.Value);
        Assert.Equal(f.Approved.Binding.SourceRevision, source.RequestedBounds!.ExpectedRevision!.Value.Value);
        Assert.Equal(ZipNumericLimits.Parent, source.RequestedBounds.MaximumReturnedContentBytes);
        Assert.Equal(1, source.Reads); Assert.All(source.Plaintext!, b => Assert.Equal((byte)0, b));
        var parent = (await f.Runtime.Journal.ReadAsync(result.OperationId))!;
        var retained = (await f.Runtime.Journal.ReadParentRetentionAsync(result.OperationId))!;
        Assert.Equal(ZipRetentionState.Created, retained.State);
        Assert.Equal(ZipParentState.AdmissionPending, parent.State);
        Assert.Equal(0, parent.Fence.Epoch); Assert.Equal(string.Empty, parent.Fence.Owner);
        Assert.Null(parent.Plan); Assert.Empty(parent.Entries); Assert.Equal(new ZipBudget(), parent.Budget);
        Assert.Equal(1, f.Runtime.Count("parent-encrypt")); Assert.Equal(0, f.Runtime.Count("child-encrypt"));
        Assert.Equal(0, f.Runtime.Count("extraction-start")); Assert.Equal(0, f.Runtime.Count("scan"));
        Assert.Equal(0, f.Runtime.Count("ingestion-encrypt"));
    }

    [Fact]
    public async Task Revoked_initial_root_acquires_no_source_or_retention()
    {
        await using var f = await Fixture.CreateAsync();
        await using var grant = new SyntheticZipParentRetentionGrant(f.Approved, f.Clock); grant.Revoke();
        var source = new SourceStore();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Service.RetainAsync(grant, source));
        Assert.Equal(0, source.Reads); Assert.Null(source.Plaintext);
        Assert.Equal(f.Approved, await f.Runtime.Journal.ReadAdmissionAsync(f.Approved.OperationId));
        Assert.Null(await f.Runtime.Journal.ReadParentRetentionAsync(f.Approved.OperationId));
    }

    [Fact]
    public async Task Revocation_after_source_acquisition_wait_prevents_read()
    {
        await using var f = await Fixture.CreateAsync();
        await using var grant = new SyntheticZipParentRetentionGrant(f.Approved, f.Clock);
        var source = new SourceStore { BeforeRead = async () => { await Task.Yield(); grant.Revoke(); } };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Service.RetainAsync(grant, source));
        Assert.Equal(0, source.Reads);
        Assert.Null(await f.Runtime.Journal.ReadParentRetentionAsync(f.Approved.OperationId));
    }

    [Fact]
    public async Task Revocation_during_source_async_disposal_denies_handoff_preserves_facts_and_clears_plaintext()
    {
        await using var f = await Fixture.CreateAsync();
        await using var grant = new SyntheticZipParentRetentionGrant(f.Approved, f.Clock);
        var source = new SourceStore { BeforeDisposal = async () => { await Task.Yield(); grant.Revoke(); } };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Service.RetainAsync(grant, source));
        var durable = (await f.Runtime.Journal.ReadAdmissionAsync(f.Approved.OperationId))!;
        Assert.Equal(ZipAdmissionState.ParentBound, durable.State);
        Assert.Equal(ZipRetentionState.Created, (await f.Runtime.Journal.ReadParentRetentionAsync(durable.OperationId))!.State);
        Assert.Equal(0, (await f.Runtime.Journal.ReadAsync(durable.OperationId))!.Fence.Epoch);
        Assert.All(source.Plaintext!, b => Assert.Equal((byte)0, b));
    }

    [Fact]
    public async Task Revocation_after_candidate_wait_preserves_candidate_and_fresh_recovery_reuses_it()
    {
        await using var f = await Fixture.CreateAsync();
        await using var grant = new SyntheticZipParentRetentionGrant(f.Approved, f.Clock);
        var source = new SourceStore();
        f.Runtime.ParentRetention.Checkpoint = async name => { if (name == "CandidateBound") { await Task.Yield(); grant.Revoke(); } };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Service.RetainAsync(grant, source));
        var admission = (await f.Runtime.Journal.ReadAdmissionAsync(f.Approved.OperationId))!;
        var candidate = (await f.Runtime.Journal.ReadParentRetentionAsync(admission.OperationId))!;
        Assert.Equal(ZipAdmissionState.SourceBound, admission.State); Assert.Equal(ZipRetentionState.CandidateBound, candidate.State);
        Assert.Null(candidate.CreateReceipt); Assert.Null(await f.Runtime.ParentStorage.GetReceiptAsync(new(candidate.Identity.CreateOperationId)));
        Assert.Null(await f.Runtime.Journal.ReadAsync(admission.OperationId));
        var bytes = (await f.Runtime.ParentStorage.ReadCandidateAsync(new(candidate.Identity.CreateOperationId)))!;
        var hash = Convert.ToHexString(SHA256.HashData(bytes)); CryptographicOperations.ZeroMemory(bytes);
        Assert.Equal(candidate.CandidateHash, hash); Assert.Equal(1, f.Runtime.Count("parent-encrypt"));
        Assert.All(source.Plaintext!, b => Assert.Equal((byte)0, b));
        f.Runtime.ParentRetention.Checkpoint = null;
        await using var recovery = new SyntheticZipParentRetentionGrant(admission, f.Clock);
        var result = await f.Service.RecoverAsync(recovery, candidate.Identity);
        var recovered = (await f.Runtime.Journal.ReadParentRetentionAsync(admission.OperationId))!;
        Assert.Equal(ZipAdmissionState.ParentBound, result.State);
        Assert.Equal(candidate.Identity, recovered.Identity); Assert.Equal(hash, recovered.CandidateHash);
        Assert.Equal(1, f.Runtime.Count("parent-encrypt")); Assert.Equal(1, source.Reads);
        Assert.Equal(0, (await f.Runtime.Journal.ReadAsync(admission.OperationId))!.Fence.Epoch);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Authority_loss_during_final_scope_disposal_denies_handoff_preserves_ParentBound(bool expire)
    {
        await using var f = await Fixture.CreateAsync();
        await using var grant = new SyntheticZipParentRetentionGrant(f.Approved, f.Clock);
        var readyForFinalDisposal = false;
        var source = new SourceStore { BeforeDisposal = () => { readyForFinalDisposal = true; return Task.CompletedTask; } };
        grant.BeforeDisposal = async () =>
        {
            if (!readyForFinalDisposal) return;
            await Task.Yield();
            if (expire) f.Clock.Now = grant.ValidUntilUtc; else grant.Revoke();
        };
        if (expire) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.RetainAsync(grant, source));
        else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Service.RetainAsync(grant, source));
        var durable = (await f.Runtime.Journal.ReadAdmissionAsync(f.Approved.OperationId))!;
        Assert.Equal(ZipAdmissionState.ParentBound, durable.State);
        Assert.Equal(ZipRetentionState.Created, (await f.Runtime.Journal.ReadParentRetentionAsync(durable.OperationId))!.State);
        Assert.Equal(0, (await f.Runtime.Journal.ReadAsync(durable.OperationId))!.Fence.Epoch);
        Assert.All(source.Plaintext!, b => Assert.Equal((byte)0, b));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recovery_cannot_switch_frozen_source_revision_or_namespace(bool namespaceChange)
    {
        await using var f = await Fixture.CreateAsync();
        await using var grant = new SyntheticZipParentRetentionGrant(f.Approved, f.Clock);
        f.Runtime.ParentRetention.Checkpoint = name => { if (name == "CandidateBound") grant.Revoke(); return Task.CompletedTask; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Service.RetainAsync(grant, new SourceStore()));
        var admission = (await f.Runtime.Journal.ReadAdmissionAsync(f.Approved.OperationId))!;
        var candidate = (await f.Runtime.Journal.ReadParentRetentionAsync(admission.OperationId))!;
        var changed = namespaceChange ? candidate.Identity with { NamespaceId = new string('A', 64) }
            : candidate.Identity with { Source = candidate.Identity.Source with { Revision = "different-revision" } };
        f.Runtime.ParentRetention.Checkpoint = null;
        await using var recovery = new SyntheticZipParentRetentionGrant(admission, f.Clock);
        var encrypts = f.Runtime.Count("parent-encrypt"); var decrypts = f.Runtime.Count("parent-decrypt");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.RecoverAsync(recovery, changed));
        Assert.Equal(candidate, await f.Runtime.Journal.ReadParentRetentionAsync(admission.OperationId));
        Assert.Equal(admission, await f.Runtime.Journal.ReadAdmissionAsync(admission.OperationId));
        Assert.Equal(encrypts, f.Runtime.Count("parent-encrypt")); Assert.Equal(decrypts, f.Runtime.Count("parent-decrypt"));
    }

    [Fact]
    public async Task Wrong_returned_source_revision_is_rejected_before_source_evidence_or_retention()
    {
        await using var f = await Fixture.CreateAsync();
        await using var grant = new SyntheticZipParentRetentionGrant(f.Approved, f.Clock);
        var source = new SourceStore { ReturnedRevision = "different-revision" };
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Service.RetainAsync(grant, source));
        Assert.Equal(f.Approved, await f.Runtime.Journal.ReadAdmissionAsync(f.Approved.OperationId));
        Assert.Null(await f.Runtime.Journal.ReadParentRetentionAsync(f.Approved.OperationId));
        Assert.Equal(0, f.Runtime.Count("parent-encrypt")); Assert.All(source.Plaintext!, b => Assert.Equal((byte)0, b));
    }

    [Fact]
    public async Task Wrong_private_namespace_cannot_create_retained_parent()
    {
        await using var f = await Fixture.CreateAsync(wrongNamespace: true);
        await using var grant = new SyntheticZipParentRetentionGrant(f.Approved, f.Clock);
        var source = new SourceStore();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.RetainAsync(grant, source));
        Assert.Null(await f.Runtime.Journal.ReadParentRetentionAsync(f.Approved.OperationId));
        Assert.Null(await f.Runtime.Journal.ReadAsync(f.Approved.OperationId));
        Assert.Equal(0, f.Runtime.Count("parent-encrypt")); Assert.Equal(0, f.Runtime.Count("scan"));
        Assert.All(source.Plaintext!, b => Assert.Equal((byte)0, b));
    }
}
