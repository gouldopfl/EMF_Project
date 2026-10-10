using System.Security.Cryptography;
using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts.Zip;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class ZipAuthorizedOwnershipTests
{
    private sealed class Grant(ZipParentAdmission admission, ZipAdmissionFixture.Clock clock) : IZipAuthorizedExecution
    {
        public ZipParentAdmission Admission { get; } = admission;
        public string ExecutingActorId => "synthetic-recovery-actor";
        public DateTimeOffset ValidUntilUtc { get; set; } = clock.Now.AddMinutes(45);
        public CancellationToken RevocationToken;
        public CancellationToken Revoked => RevocationToken;
        public int Checks; public bool Denied; public Action? OnCheck;
        public Task RevalidateAsync(CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); Checks++; OnCheck?.Invoke(); if (Denied) throw new UnauthorizedAccessException(); return Task.CompletedTask; }
        public Task<ZipChildIngestionRuntime> OpenChildAsync(ZipParentSnapshot p, ZipEntryProgress e, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static async Task<(ZipParentAdmission Admission, ZipDurableProfile Profile)> Admit(ZipDurableRuntimeFixture f, ZipAdmissionFixture.Clock clock)
    {
        await f.Journal.InitializeAsync();
        var profile = ZipDurableProfile.Create(f.ParentStorage.NamespaceId, f.ChildStorage.NamespaceId,
            new("clamd", "test-policy", new string('C', 64)), "synthetic-test-protection");
        var binding = new ZipAdmissionBinding("workflow", "workflow-operation", "zip-archives", "parent",
            "source-parent", "source-revision", "synthetic-original-actor", "synthetic-authorized-operation",
            profile.CanonicalJson, profile.Fingerprint, profile.ParentNamespaceId, profile.ChildNamespaceId);
        var p = await f.Journal.ResolveOrReserveAdmissionAsync(new("synthetic-test-issuer", "stable-request"), binding);
        var approval = new ZipAdmissionApproval(p.OperationId, ZipAdmissionValidation.BindingHash(binding), p.Key.IssuerId,
            "synthetic-decision", "authority-revision", "policy-v1", ZipAuthorityCapabilities.Admit | ZipAuthorityCapabilities.Recover,
            clock.Now, clock.Now.AddHours(1));
        p = await f.Journal.AppendAdmissionEventAsync(p, new("approval", ZipAdmissionEventKind.Approved, binding.OriginalActorId, clock.Now, approval));
        var bytes = ZipDurableRuntimeFixture.Zip();
        var source = new ZipRetainedBinding(binding.SourceContentId, binding.SourceRevision, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length);
        p = await f.Journal.AppendAdmissionEventAsync(p, new("source", ZipAdmissionEventKind.SourceBound, binding.OriginalActorId, clock.Now, Source: source));
        using var lease = new ArtifactContentReadLease(new(source.ContentId), new(source.Revision), source.Length, bytes);
        var parent = await f.ParentRetention.RetainAsync(p.OperationId, binding.ParentArtifactId, profile.Fingerprint, lease);
        p = await f.Journal.AppendAdmissionEventAsync(p, new("parent", ZipAdmissionEventKind.ParentBound, binding.OriginalActorId, clock.Now, Parent: parent));
        return (p, profile);
    }

    [Fact]
    public async Task Takeover_requires_current_host_authority_and_preserves_original_approval()
    {
        await using var evidence = await ArtifactIngestionFixture.CreateAsync();
        var clock = new ZipAdmissionFixture.Clock(); var f = new ZipDurableRuntimeFixture(evidence, clock);
        var (p, profile) = await Admit(f, clock); var grant = new Grant(p, clock);
        var service = new ZipAuthorizedOwnershipService(f.Journal, f.Journal, profile, clock);
        var first = await service.ClaimAsync(grant, "worker"); Assert.Equal(2, grant.Checks);
        clock.Now += TimeSpan.FromMinutes(16); grant.Denied = true;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ClaimAsync(grant, "next-worker"));
        Assert.Equal(first.Fence, (await f.Journal.ReadAsync(p.OperationId))!.Fence);
        grant.Denied = false; var next = await service.ClaimAsync(grant, "next-worker");
        Assert.Equal(first.Fence.Epoch + 1, next.Fence.Epoch);
        Assert.Equal(p, await f.Journal.ReadAdmissionAsync(p.OperationId));
        Assert.Equal(p.Approval, (await f.Journal.ReadAdmissionAsync(p.OperationId))!.Approval);
        await Assert.ThrowsAsync<ZipFenceException>(() => f.Journal.RenewOwnershipAsync(p.OperationId, "worker", first.Fence.Epoch, profile.OwnershipLease));
    }

    [Fact]
    public async Task Revocation_vetoes_renewal_and_recovery_without_changing_ownership_or_original_evidence()
    {
        await using var evidence = await ArtifactIngestionFixture.CreateAsync();
        var clock = new ZipAdmissionFixture.Clock(); var f = new ZipDurableRuntimeFixture(evidence, clock);
        var (p, profile) = await Admit(f, clock); var grant = new Grant(p, clock);
        var service = new ZipAuthorizedOwnershipService(f.Journal, f.Journal, profile, clock);
        var owned = await service.ClaimAsync(grant, "worker");
        var revoked = await f.Journal.AppendAdmissionEventAsync(p, new("revocation", ZipAdmissionEventKind.Revoked,
            "synthetic-issuer-actor", clock.Now, SafeReason: "AuthorityRevoked"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Journal.RenewOwnershipAsync(p.OperationId, "worker", owned.Fence.Epoch, profile.OwnershipLease));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ClaimAsync(grant, "worker"));
        Assert.Equal(owned.Fence, (await f.Journal.ReadAsync(p.OperationId))!.Fence);
        Assert.Equal(owned.OwnerUntil, (await f.Journal.ReadAsync(p.OperationId))!.OwnerUntil);
        Assert.Equal(p.Approval, revoked.Approval); Assert.Equal(p.Binding, revoked.Binding);
    }

    [Fact]
    public async Task Profile_mismatch_fails_before_host_or_parent_payload_work()
    {
        await using var evidence = await ArtifactIngestionFixture.CreateAsync();
        var clock = new ZipAdmissionFixture.Clock(); var f = new ZipDurableRuntimeFixture(evidence, clock);
        var (p, profile) = await Admit(f, clock); var grant = new Grant(p, clock);
        var before = await f.Journal.ReadAsync(p.OperationId); var decryptions = f.Count("parent-decrypt");
        var service = new ZipAuthorizedOwnershipService(f.Journal, f.Journal, profile with { ProtectionContractId = "changed" }, clock);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ClaimAsync(grant, "worker"));
        Assert.Equal(0, grant.Checks); Assert.Equal(decryptions, f.Count("parent-decrypt"));
        Assert.Equal(before!.Fence, (await f.Journal.ReadAsync(p.OperationId))!.Fence);
    }

    [Fact]
    public async Task Live_expiry_during_claim_never_returns_executable_ownership()
    {
        await using var evidence = await ArtifactIngestionFixture.CreateAsync();
        var clock = new ZipAdmissionFixture.Clock(); var f = new ZipDurableRuntimeFixture(evidence, clock);
        var (p, profile) = await Admit(f, clock); var grant = new Grant(p, clock);
        grant.OnCheck = () => { if (grant.Checks == 2) clock.Now = grant.ValidUntilUtc; };
        var service = new ZipAuthorizedOwnershipService(f.Journal, f.Journal, profile, clock);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ClaimAsync(grant, "worker"));
        Assert.Equal(p, await f.Journal.ReadAdmissionAsync(p.OperationId));
    }

    [Fact]
    public async Task Expired_recorded_authority_vetoes_renewal_and_takeover_despite_live_test_grant()
    {
        await using var evidence = await ArtifactIngestionFixture.CreateAsync();
        var clock = new ZipAdmissionFixture.Clock(); var f = new ZipDurableRuntimeFixture(evidence, clock);
        var (p, profile) = await Admit(f, clock); var grant = new Grant(p, clock);
        var service = new ZipAuthorizedOwnershipService(f.Journal, f.Journal, profile, clock);
        var owned = await service.ClaimAsync(grant, "worker"); clock.Now += TimeSpan.FromMinutes(59);
        grant.ValidUntilUtc = clock.Now.AddHours(1);
        owned = await service.ClaimAsync(grant, "worker"); clock.Now += TimeSpan.FromMinutes(2);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Journal.RenewOwnershipAsync(p.OperationId, "worker", owned.Fence.Epoch, profile.OwnershipLease));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ClaimAsync(grant, "worker"));
        Assert.Equal(p, await f.Journal.ReadAdmissionAsync(p.OperationId));
        Assert.Equal(owned.Fence, (await f.Journal.ReadAsync(p.OperationId))!.Fence);
    }

    [Fact]
    public async Task Revocation_during_claim_never_returns_executable_ownership()
    {
        await using var evidence = await ArtifactIngestionFixture.CreateAsync();
        var clock = new ZipAdmissionFixture.Clock(); var f = new ZipDurableRuntimeFixture(evidence, clock);
        var (p, profile) = await Admit(f, clock); using var revoked = new CancellationTokenSource();
        var grant = new Grant(p, clock) { RevocationToken = revoked.Token };
        grant.OnCheck = () => { if (grant.Checks == 2) revoked.Cancel(); };
        var service = new ZipAuthorizedOwnershipService(f.Journal, f.Journal, profile, clock);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ClaimAsync(grant, "worker"));
        Assert.Equal(p, await f.Journal.ReadAdmissionAsync(p.OperationId));
    }

    [Fact]
    public async Task Concurrent_renewal_and_takeover_never_leave_two_valid_owners()
    {
        await using var evidence = await ArtifactIngestionFixture.CreateAsync();
        var clock = new ZipAdmissionFixture.Clock(); var f = new ZipDurableRuntimeFixture(evidence, clock);
        var (p, profile) = await Admit(f, clock); var grant = new Grant(p, clock);
        var service = new ZipAuthorizedOwnershipService(f.Journal, f.Journal, profile, clock);
        var first = await service.ClaimAsync(grant, "worker"); clock.Now += TimeSpan.FromMinutes(14);
        await Task.WhenAll(Task.Run(() => f.Journal.RenewOwnershipAsync(p.OperationId, "worker", first.Fence.Epoch, profile.OwnershipLease)),
            Task.Run(() => Assert.ThrowsAsync<ZipFenceException>(() => service.ClaimAsync(grant, "next-worker"))));
        var renewed = (await f.Journal.ReadAsync(p.OperationId))!; Assert.Equal(first.Fence, renewed.Fence);
        clock.Now = renewed.OwnerUntil;
        await Task.WhenAll(Task.Run(() => Assert.ThrowsAsync<ZipFenceException>(() => f.Journal.RenewOwnershipAsync(p.OperationId, "worker", first.Fence.Epoch, profile.OwnershipLease))),
            Task.Run(() => service.ClaimAsync(grant, "next-worker")));
        var next = (await f.Journal.ReadAsync(p.OperationId))!;
        Assert.Equal("next-worker", next.Fence.Owner); Assert.Equal(first.Fence.Epoch + 1, next.Fence.Epoch);
        Assert.Equal(p, await f.Journal.ReadAdmissionAsync(p.OperationId));
    }
}
