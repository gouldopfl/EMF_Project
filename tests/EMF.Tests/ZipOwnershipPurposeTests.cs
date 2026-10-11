using EMF.Core.Contracts.Zip;
using EMF.Persistence.Repositories;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class ZipOwnershipPurposeTests
{
    private sealed class SteppingClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now += TimeSpan.FromTicks(10);
    }

    [Fact]
    public async Task Lease_that_expires_before_commit_is_rolled_back()
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        var request = await f.RequestAsync(ZipOwnershipPurpose.InitialExecution);
        var journal = new SqliteZipExtractionJournal(f.Evidence.DatabasePath, new SteppingClock(f.Clock.Now));
        await Assert.ThrowsAsync<ZipFenceException>(() => journal.ClaimOwnershipAsync(request with { Duration = TimeSpan.FromTicks(1) }));
        Assert.Equal(request.ExpectedFence, (await f.Journal.ReadAsync(f.Admission.OperationId))!.Fence);
    }

    [Theory]
    [InlineData(ZipAdmissionEventKind.RequiresReview)]
    [InlineData(ZipAdmissionEventKind.Revoked)]
    public async Task Current_stopped_admission_cannot_claim_even_with_exact_projection(ZipAdmissionEventKind kind)
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        var request = await f.RequestAsync(ZipOwnershipPurpose.InitialExecution);
        var stopped = await f.Journal.AppendAdmissionEventAsync(f.Admission, new("stop", kind, "synthetic-current-actor", f.Clock.Now, SafeReason: "AuthorityStopped"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Journal.ClaimOwnershipAsync(request with { ExpectedAdmission = stopped }));
        Assert.Equal(request.ExpectedFence, (await f.Journal.ReadAsync(f.Admission.OperationId))!.Fence);
    }

    [Fact]
    public async Task Completed_pending_release_can_recover_but_fully_released_cannot()
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        var parent = await f.Journal.ClaimOwnershipAsync(await f.RequestAsync(ZipOwnershipPurpose.InitialExecution));
        parent = await f.Journal.ReserveAsync(parent.Fence, new("preflight", ZipWorkKind.Preflight, null, parent.Binding.Input.Length + 65539));
        const string receipt = "{\"PreflightVersion\":1}";
        parent = await f.Journal.AdmitPlanAsync(parent.Fence, new(SqliteZipExtractionJournal.PlanHash(receipt, []), receipt, []));
        parent = await f.Journal.CompleteParentAsync(parent.Fence);
        Assert.Equal(ZipParentState.Completed, parent.State);
        parent = await f.Journal.ClaimOwnershipAsync(await f.RequestAsync(ZipOwnershipPurpose.Recovery));
        var released = await f.Runtime.ParentRetention.ReleaseAsync(parent);
        Assert.Equal(ZipParentState.Released, released.Parent.State);
        var request = await f.RequestAsync(ZipOwnershipPurpose.Recovery);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Journal.ClaimOwnershipAsync(request));
        Assert.Equal(released.Parent.Fence, (await f.Journal.ReadAsync(f.Admission.OperationId))!.Fence);
    }

    [Fact]
    public async Task Review_stopped_parent_cannot_be_reopened_by_permission()
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        var parent = await f.Journal.ClaimOwnershipAsync(await f.RequestAsync(ZipOwnershipPurpose.InitialExecution));
        parent = await f.Journal.ReviewAsync(parent.Fence, "SyntheticReview");
        var request = await f.RequestAsync(ZipOwnershipPurpose.Recovery);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Journal.ClaimOwnershipAsync(request));
        Assert.Equal(parent.Fence, (await f.Journal.ReadAsync(f.Admission.OperationId))!.Fence);
    }

    [Fact]
    public async Task Current_revalidated_evidence_can_outlive_original_approval_without_rewriting_it()
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        await f.Journal.ClaimOwnershipAsync(await f.RequestAsync(ZipOwnershipPurpose.InitialExecution));
        f.Clock.Now = f.Admission.Approval!.ExpiresUtc;
        var latest = await f.Journal.AppendAdmissionEventAsync(f.Admission, new("fresh-validation", ZipAdmissionEventKind.AuthorityValidated,
            "synthetic-current-actor", f.Clock.Now, f.Admission.Approval with { DecisionId = "fresh", IssuedUtc = f.Clock.Now, ExpiresUtc = f.Clock.Now.AddHours(1) }));
        var request = await f.RequestAsync(ZipOwnershipPurpose.Recovery);
        var parent = await f.Journal.ClaimOwnershipAsync(request with { ExpectedAdmission = latest });
        Assert.Equal(2, parent.Fence.Epoch);
        Assert.Equal(f.Admission.Approval, latest.Approval);
        Assert.Equal(latest, await f.Journal.ReadAdmissionAsync(f.Admission.OperationId));
    }

    [Fact]
    public async Task Initial_claim_needs_Admit_without_Recovery_and_preserves_evidence()
    {
        await using var f = await ZipOwnershipFixture.CreateAsync(ZipAuthorityCapabilities.Admit);
        var request = await f.RequestAsync(ZipOwnershipPurpose.InitialExecution);
        var owned = await f.Journal.ClaimOwnershipAsync(request);
        Assert.Equal(1, owned.Fence.Epoch); Assert.Equal(request.ExpectedFence.Revision + 1, owned.Fence.Revision);
        Assert.Equal(request.Owner, owned.Fence.Owner);
        Assert.Equal(f.Admission, await f.Journal.ReadAdmissionAsync(f.Admission.OperationId));
        Assert.Equal(new ZipBudget(), owned.Budget); Assert.Empty(owned.Entries);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Two_real_connections_allow_exactly_one_first_claim_even_for_same_owner(bool sameOwner)
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        var first = await f.RequestAsync(ZipOwnershipPurpose.InitialExecution);
        var second = first with { Owner = sameOwner ? first.Owner : "second-worker" };
        using var start = new Barrier(2);
        async Task<bool> Race(ZipOwnershipClaimRequest request)
        {
            start.SignalAndWait(TimeSpan.FromSeconds(10));
            var journal = new SqliteZipExtractionJournal(f.Evidence.DatabasePath, f.Clock);
            try { await journal.ClaimOwnershipAsync(request); return true; }
            catch (ZipFenceException) { return false; }
        }
        var results = await Task.WhenAll(Task.Run(() => Race(first)), Task.Run(() => Race(second)));
        Assert.Single(results.Where(success => success));
        var owned = (await f.Journal.ReadAsync(f.Admission.OperationId))!;
        Assert.Equal(1, owned.Fence.Epoch); Assert.Equal(first.ExpectedFence.Revision + 1, owned.Fence.Revision);
        Assert.Equal(f.Admission, await f.Journal.ReadAdmissionAsync(f.Admission.OperationId));
    }

    [Fact]
    public async Task Recovery_cannot_manufacture_prior_ownership()
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        var request = await f.RequestAsync(ZipOwnershipPurpose.Recovery);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Journal.ClaimOwnershipAsync(request));
        Assert.Equal(request.ExpectedFence, (await f.Journal.ReadAsync(f.Admission.OperationId))!.Fence);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Initial_cannot_be_reused_after_claim_even_with_same_owner_or_expiry(bool expired)
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        var owned = await f.Journal.ClaimOwnershipAsync(await f.RequestAsync(ZipOwnershipPurpose.InitialExecution));
        if (expired) f.Clock.Now = owned.OwnerUntil;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ClaimFresh());
        Assert.Equal(owned.Fence, (await f.Journal.ReadAsync(f.Admission.OperationId))!.Fence);
        async Task ClaimFresh() => await f.Journal.ClaimOwnershipAsync(await f.RequestAsync(ZipOwnershipPurpose.InitialExecution));
    }

    [Fact]
    public async Task Recovery_requires_its_recorded_ceiling_and_preserves_original_approval()
    {
        await using var f = await ZipOwnershipFixture.CreateAsync(ZipAuthorityCapabilities.Admit);
        var owned = await f.Journal.ClaimOwnershipAsync(await f.RequestAsync(ZipOwnershipPurpose.InitialExecution));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Recover());
        Assert.Equal(owned.Fence, (await f.Journal.ReadAsync(f.Admission.OperationId))!.Fence);
        Assert.Equal(f.Admission, await f.Journal.ReadAdmissionAsync(f.Admission.OperationId));
        async Task Recover() => await f.Journal.ClaimOwnershipAsync(await f.RequestAsync(ZipOwnershipPurpose.Recovery));
    }

    [Fact]
    public async Task Recovery_respects_live_different_owner_and_allows_expired_takeover()
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        var owned = await f.Journal.ClaimOwnershipAsync(await f.RequestAsync(ZipOwnershipPurpose.InitialExecution));
        await Assert.ThrowsAsync<ZipFenceException>(() => Recover());
        f.Clock.Now = owned.OwnerUntil;
        var recovered = await Recover();
        Assert.Equal(owned.Fence.Epoch + 1, recovered.Fence.Epoch); Assert.Equal("next-worker", recovered.Fence.Owner);
        Assert.Equal(f.Admission, await f.Journal.ReadAdmissionAsync(f.Admission.OperationId));
        async Task<ZipParentSnapshot> Recover() => await f.Journal.ClaimOwnershipAsync(await f.RequestAsync(ZipOwnershipPurpose.Recovery, "next-worker"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Stale_admission_is_rejected_inside_claim_transaction(bool revoked)
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        var request = await f.RequestAsync(ZipOwnershipPurpose.InitialExecution);
        var change = revoked
            ? new ZipAdmissionEvent("changed", ZipAdmissionEventKind.Revoked, "synthetic-issuer", f.Clock.Now, SafeReason: "Revoked")
            : new ZipAdmissionEvent("changed", ZipAdmissionEventKind.AuthorityValidated, "synthetic-recovery", f.Clock.Now,
                f.Admission.Approval! with { DecisionId = "fresh-decision" });
        var current = await new SqliteZipExtractionJournal(f.Evidence.DatabasePath, f.Clock).AppendAdmissionEventAsync(f.Admission, change);
        await Assert.ThrowsAsync<ZipFenceException>(() => f.Journal.ClaimOwnershipAsync(request));
        Assert.Equal(request.ExpectedFence, (await f.Journal.ReadAsync(f.Admission.OperationId))!.Fence);
        Assert.Equal(current, await f.Journal.ReadAdmissionAsync(f.Admission.OperationId));
    }

    [Fact]
    public async Task Stale_fence_and_renewal_only_projection_are_rejected()
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        var initial = await f.RequestAsync(ZipOwnershipPurpose.InitialExecution);
        var owned = await f.Journal.ClaimOwnershipAsync(initial);
        await Assert.ThrowsAsync<ZipFenceException>(() => f.Journal.ClaimOwnershipAsync(initial));
        var recovery = await f.RequestAsync(ZipOwnershipPurpose.Recovery);
        f.Clock.Now += TimeSpan.FromMinutes(1);
        var until = await f.Journal.RenewOwnershipAsync(owned.Binding.OperationId, owned.Fence.Owner, owned.Fence.Epoch, f.Profile.OwnershipLease);
        await Assert.ThrowsAsync<ZipFenceException>(() => f.Journal.ClaimOwnershipAsync(recovery));
        var current = (await f.Journal.ReadAsync(f.Admission.OperationId))!;
        Assert.Equal(owned.Fence, current.Fence); Assert.Equal(until, current.OwnerUntil);
    }

    [Theory]
    [InlineData(0)] [InlineData(3)]
    public async Task Undefined_purpose_does_no_work(int purpose)
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        var request = await f.RequestAsync((ZipOwnershipPurpose)purpose);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => f.Journal.ClaimOwnershipAsync(request));
        Assert.Equal(request.ExpectedFence, (await f.Journal.ReadAsync(f.Admission.OperationId))!.Fence);
    }

    [Fact]
    public async Task Expired_evidence_and_oversized_lease_cannot_create_eligibility()
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        var request = await f.RequestAsync(ZipOwnershipPurpose.InitialExecution);
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Journal.ClaimOwnershipAsync(request with { Duration = f.Profile.OwnershipLease + TimeSpan.FromTicks(1) }));
        f.Clock.Now = f.Admission.LatestAuthorityEvidence!.ExpiresUtc;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Journal.ClaimOwnershipAsync(request));
        Assert.Equal(request.ExpectedFence, (await f.Journal.ReadAsync(f.Admission.OperationId))!.Fence);
    }
}
