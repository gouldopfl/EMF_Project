using EMF.Core.Contracts.Zip;
using EMF.Orchestration.Services;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class ZipAuthorizedOwnershipTests
{
    private static ZipAuthorizedOwnershipService Service(ZipOwnershipFixture f) => new(f.Journal, f.Journal, f.Profile, f.Clock);
    private static SyntheticZipExecutionGrant Grant(ZipOwnershipFixture f, ZipOwnershipPurpose purpose) => new(f.Admission, purpose, f.Clock);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalidation_during_async_scope_disposal_denies_post_commit_handoff(bool expire)
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        await using var grant = Grant(f, ZipOwnershipPurpose.InitialExecution);
        var disposing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        grant.BeforeScopeDisposal = () => { disposing.TrySetResult(); return release.Task; };
        var claim = Service(f).ClaimAsync(grant, await f.RequestAsync(grant.Purpose));
        await disposing.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, (await f.Journal.ReadAsync(f.Admission.OperationId))!.Fence.Epoch);
        if (expire) f.Clock.Now = grant.ValidUntilUtc; else grant.Revoke();
        release.SetResult();
        if (expire) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => claim.WaitAsync(TimeSpan.FromSeconds(10)));
        else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => claim.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, (await f.Journal.ReadAsync(f.Admission.OperationId))!.Fence.Epoch);
        await using var nextInitial = Grant(f, ZipOwnershipPurpose.InitialExecution);
        var request = await f.RequestAsync(nextInitial.Purpose);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(f).ClaimAsync(nextInitial, request));
        await using var recovery = Grant(f, ZipOwnershipPurpose.Recovery);
        var recovered = await Service(f).ClaimAsync(recovery, await f.RequestAsync(recovery.Purpose));
        Assert.Equal(2, recovered.Fence.Epoch);
        Assert.Equal(f.Admission, await f.Journal.ReadAdmissionAsync(f.Admission.OperationId));
    }

    [Fact]
    public async Task Recorded_revocation_after_ownership_vetoes_renewal_and_fresh_recovery()
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        await using var initial = Grant(f, ZipOwnershipPurpose.InitialExecution);
        var owned = await Service(f).ClaimAsync(initial, await f.RequestAsync(initial.Purpose));
        var revoked = await f.Journal.AppendAdmissionEventAsync(f.Admission, new("revocation-after-claim", ZipAdmissionEventKind.Revoked,
            "synthetic-issuer", f.Clock.Now, SafeReason: "AuthorityRevoked"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Journal.RenewOwnershipAsync(f.Admission.OperationId,
            owned.Fence.Owner, owned.Fence.Epoch, f.Profile.OwnershipLease));
        await using var recovery = new SyntheticZipExecutionGrant(revoked, ZipOwnershipPurpose.Recovery, f.Clock);
        var request = await f.RequestAsync(recovery.Purpose);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(f).ClaimAsync(recovery, request with { ExpectedAdmission = revoked }));
        var after = (await f.Journal.ReadAsync(f.Admission.OperationId))!;
        Assert.Equal(owned.Fence, after.Fence); Assert.Equal(owned.OwnerUntil, after.OwnerUntil);
        Assert.Equal(f.Admission.Approval, revoked.Approval); Assert.Equal(f.Admission.Binding, revoked.Binding);
    }

    [Fact]
    public async Task Concurrent_renewal_and_authorized_takeover_keep_exactly_one_current_owner()
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        await using var initial = Grant(f, ZipOwnershipPurpose.InitialExecution);
        var service = Service(f);
        var first = await service.ClaimAsync(initial, await f.RequestAsync(initial.Purpose));
        var independent = new EMF.Persistence.Repositories.SqliteZipExtractionJournal(f.Evidence.DatabasePath, f.Clock);
        await using var recovery = Grant(f, ZipOwnershipPurpose.Recovery);
        f.Clock.Now += TimeSpan.FromMinutes(14);
        var request = await f.RequestAsync(recovery.Purpose, "next-worker");
        using var liveRace = new Barrier(2);
        await Task.WhenAll(
            Task.Run(async () => { Assert.True(liveRace.SignalAndWait(TimeSpan.FromSeconds(10))); await independent.RenewOwnershipAsync(f.Admission.OperationId, first.Fence.Owner, first.Fence.Epoch, f.Profile.OwnershipLease); }),
            Task.Run(async () => { Assert.True(liveRace.SignalAndWait(TimeSpan.FromSeconds(10))); await Assert.ThrowsAsync<ZipFenceException>(() => service.ClaimAsync(recovery, request)); }));
        var renewed = (await f.Journal.ReadAsync(f.Admission.OperationId))!;
        Assert.Equal(first.Fence, renewed.Fence); Assert.True(renewed.OwnerUntil > first.OwnerUntil);
        f.Clock.Now = renewed.OwnerUntil;
        request = await f.RequestAsync(recovery.Purpose, "next-worker");
        using var expiredRace = new Barrier(2);
        await Task.WhenAll(
            Task.Run(async () => { Assert.True(expiredRace.SignalAndWait(TimeSpan.FromSeconds(10))); await Assert.ThrowsAsync<ZipFenceException>(() => independent.RenewOwnershipAsync(f.Admission.OperationId, first.Fence.Owner, first.Fence.Epoch, f.Profile.OwnershipLease)); }),
            Task.Run(async () => { Assert.True(expiredRace.SignalAndWait(TimeSpan.FromSeconds(10))); await service.ClaimAsync(recovery, request); }));
        var next = (await f.Journal.ReadAsync(f.Admission.OperationId))!;
        Assert.Equal("next-worker", next.Fence.Owner); Assert.Equal(first.Fence.Epoch + 1, next.Fence.Epoch);
        await Assert.ThrowsAsync<ZipFenceException>(() => independent.RenewOwnershipAsync(f.Admission.OperationId, first.Fence.Owner, first.Fence.Epoch, f.Profile.OwnershipLease));
        Assert.Equal(f.Admission, await f.Journal.ReadAdmissionAsync(f.Admission.OperationId));
    }

    [Fact]
    public async Task Expired_recorded_authority_vetoes_fresh_live_recovery_and_renewal()
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        await using var initial = Grant(f, ZipOwnershipPurpose.InitialExecution);
        await Service(f).ClaimAsync(initial, await f.RequestAsync(initial.Purpose));
        f.Clock.Now += TimeSpan.FromMinutes(59);
        await using var recovery = Grant(f, ZipOwnershipPurpose.Recovery);
        var owned = await Service(f).ClaimAsync(recovery, await f.RequestAsync(recovery.Purpose));
        f.Clock.Now += TimeSpan.FromMinutes(2);
        await using var fresh = Grant(f, ZipOwnershipPurpose.Recovery);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(f).ClaimAsync(fresh,
            new(fresh.Purpose, f.Admission, owned.Fence, owned.OwnerUntil, "worker", f.Profile.OwnershipLease)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Journal.RenewOwnershipAsync(f.Admission.OperationId,
            "worker", owned.Fence.Epoch, f.Profile.OwnershipLease));
        Assert.Equal(owned.Fence, (await f.Journal.ReadAsync(f.Admission.OperationId))!.Fence);
        Assert.Equal(f.Admission, await f.Journal.ReadAdmissionAsync(f.Admission.OperationId));
    }

    [Fact]
    public async Task Initial_only_live_grant_claims_without_recovery_and_preserves_evidence()
    {
        await using var f = await ZipOwnershipFixture.CreateAsync(ZipAuthorityCapabilities.Admit);
        await using var grant = Grant(f, ZipOwnershipPurpose.InitialExecution);
        var decryptions = f.Runtime.Count("parent-decrypt");
        var first = await Service(f).ClaimAsync(grant, await f.RequestAsync(grant.Purpose));
        Assert.Equal(1, first.Fence.Epoch);
        Assert.Equal(f.Admission, await f.Journal.ReadAdmissionAsync(f.Admission.OperationId));
        Assert.Equal(decryptions, f.Runtime.Count("parent-decrypt"));
    }

    [Fact]
    public async Task Recovery_uses_fresh_grant_and_same_owner_does_not_reuse_initial_permission()
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        await using var initial = Grant(f, ZipOwnershipPurpose.InitialExecution);
        var service = Service(f);
        var first = await service.ClaimAsync(initial, await f.RequestAsync(initial.Purpose));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ClaimAsync(initial,
            new(ZipOwnershipPurpose.Recovery, f.Admission, first.Fence, first.OwnerUntil, "worker", f.Profile.OwnershipLease)));
        await using var recovery = Grant(f, ZipOwnershipPurpose.Recovery);
        recovery.Denied = true;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ClaimAsync(recovery,
            new(recovery.Purpose, f.Admission, first.Fence, first.OwnerUntil, "worker", f.Profile.OwnershipLease)));
        recovery.Denied = false;
        var next = await service.ClaimAsync(recovery, await f.RequestAsync(recovery.Purpose));
        Assert.Equal(first.Fence.Epoch + 1, next.Fence.Epoch);
        Assert.Equal(f.Admission, await f.Journal.ReadAdmissionAsync(f.Admission.OperationId));
    }

    [Fact]
    public async Task Profile_mismatch_fails_before_host_or_payload_work()
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        await using var grant = Grant(f, ZipOwnershipPurpose.InitialExecution);
        var request = await f.RequestAsync(grant.Purpose);
        var decryptions = f.Runtime.Count("parent-decrypt");
        var service = new ZipAuthorizedOwnershipService(f.Journal, f.Journal, f.Profile with { ProtectionContractId = "changed" }, f.Clock);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ClaimAsync(grant, request));
        Assert.Equal(0, grant.Checks); Assert.Equal(decryptions, f.Runtime.Count("parent-decrypt"));
        Assert.Equal(request.ExpectedFence, (await f.Journal.ReadAsync(f.Admission.OperationId))!.Fence);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalidation_after_real_commit_never_returns_ownership(bool expire)
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        await using var grant = Grant(f, ZipOwnershipPurpose.InitialExecution);
        grant.OnCheck = () =>
        {
            // Before/after the real journal effect are checks 4/5.
            if (grant.Checks == 5) { if (expire) f.Clock.Now = grant.ValidUntilUtc; else grant.Revoke(); }
        };
        var request = await f.RequestAsync(grant.Purpose);
        if (expire) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(f).ClaimAsync(grant, request));
        else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(f).ClaimAsync(grant, request));
        Assert.Equal(1, (await f.Journal.ReadAsync(f.Admission.OperationId))!.Fence.Epoch);
        Assert.Equal(f.Admission, await f.Journal.ReadAdmissionAsync(f.Admission.OperationId));
        await using var nextInitial = Grant(f, ZipOwnershipPurpose.InitialExecution);
        var secondInitial = await f.RequestAsync(nextInitial.Purpose);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(f).ClaimAsync(nextInitial, secondInitial));
        await using var recovery = Grant(f, ZipOwnershipPurpose.Recovery);
        var recovered = await Service(f).ClaimAsync(recovery, await f.RequestAsync(recovery.Purpose));
        Assert.Equal(2, recovered.Fence.Epoch);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Revocation_or_root_disposal_during_scope_wait_prevents_claim(bool dispose)
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        await using var grant = Grant(f, ZipOwnershipPurpose.InitialExecution);
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        grant.BeforeScopeAcceptance = () => { waiting.SetResult(); return release.Task; };
        var request = await f.RequestAsync(grant.Purpose);
        var claim = Service(f).ClaimAsync(grant, request);
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (dispose) await grant.DisposeAsync(); else grant.Revoke();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => claim);
        release.SetResult();
        Assert.Equal(request.ExpectedFence, (await f.Journal.ReadAsync(f.Admission.OperationId))!.Fence);
    }

    [Fact]
    public async Task Scope_request_mismatch_is_denied_before_claim()
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        await using var grant = Grant(f, ZipOwnershipPurpose.InitialExecution);
        grant.ChangeScope = scope => new SyntheticZipExecutionGrant.Scope(grant, scope.Request with { Owner = "other" }, f.Clock);
        var request = await f.RequestAsync(grant.Purpose);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(f).ClaimAsync(grant, request));
        Assert.Equal(request.ExpectedFence, (await f.Journal.ReadAsync(f.Admission.OperationId))!.Fence);
    }

    [Fact]
    public async Task Recorded_revocation_vetoes_current_synthetic_permission()
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        await using var grant = Grant(f, ZipOwnershipPurpose.InitialExecution);
        var request = await f.RequestAsync(grant.Purpose);
        var revoked = await f.Journal.AppendAdmissionEventAsync(f.Admission, new("revoked", ZipAdmissionEventKind.Revoked,
            "synthetic-issuer", f.Clock.Now, SafeReason: "AuthorityRevoked"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(f).ClaimAsync(grant, request));
        Assert.Equal(f.Admission.Approval, revoked.Approval);
        Assert.Equal(request.ExpectedFence, (await f.Journal.ReadAsync(f.Admission.OperationId))!.Fence);
    }
}
