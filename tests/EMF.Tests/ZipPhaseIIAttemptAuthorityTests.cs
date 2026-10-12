using EMF.Core.Contracts.Zip;
using EMF.Orchestration.Services;
using EMF.Tests.TestInfrastructure;
namespace EMF.Tests;

[Collection(ZipProcessWideGateCollection.Name)]
public sealed class ZipPhaseIIAttemptAuthorityTests
{
    private static ZipAuthorizedOwnershipService Ownership(ZipOwnershipFixture f) => new(f.Journal, f.Journal, f.Profile, f.Clock);
    private static async Task<ZipExecutionAttempt> Claim(ZipOwnershipFixture f, SyntheticZipExecutionGrant grant)
        => await ZipExecutionAttempt.ClaimAsync(Ownership(f), grant, await f.RequestAsync(grant.Purpose), f.Clock);
    private static async Task Denied(Task operation)
    {
        var error = await Record.ExceptionAsync(() => operation.WaitAsync(TimeSpan.FromSeconds(20)));
        Assert.True(error is UnauthorizedAccessException or OperationCanceledException, $"Expected authority denial; actual: {error}");
    }
    private static async Task AssertNoDownstream(ZipOwnershipFixture f, ZipParentSnapshot parent)
    {
        Assert.Equal(0, parent.Budget.ScannerAttempts);
        Assert.Equal(0, parent.Budget.ScannerReserved);
        Assert.Equal(0, parent.Budget.IngestionAttempts);
        Assert.Equal(-1, parent.Fence.ConfirmedOrdinal);
        Assert.NotEqual(ZipParentState.RequiresReview, parent.State);
        Assert.NotEqual(ZipParentState.Completed, parent.State);
        Assert.NotEqual(ZipParentState.Released, parent.State);
        Assert.Equal(0, f.Runtime.Count("scan"));
        Assert.Equal(0, f.Runtime.Count("ingestion-encrypt"));
        Assert.Equal(0, f.Runtime.Count("Ingesting"));
        Assert.Equal(0, f.Runtime.Count("Resuming"));
        var retainedParent = (await f.Journal.ReadParentRetentionAsync(f.Admission.OperationId))!;
        Assert.Equal(ZipRetentionState.Created, retainedParent.State);
        Assert.Null(retainedParent.ReleaseReceipt);
        Assert.Null(retainedParent.CandidateCleanup);
        foreach (var entry in parent.Entries.Where(entry => entry.Plan.FileOrdinal is not null))
        {
            Assert.NotEqual(ZipEntryState.Scanned, entry.State);
            Assert.NotEqual(ZipEntryState.Ingested, entry.State);
            Assert.NotEqual(ZipEntryState.Acknowledged, entry.State);
            Assert.NotEqual(ZipEntryState.RequiresReview, entry.State);
            Assert.NotEqual(ZipEntryState.Released, entry.State);
            Assert.Null(entry.Ingestion);
            var retention = await f.Journal.ReadRetentionAsync(f.Admission.OperationId, entry.Plan.FileOrdinal!.Value);
            if (retention is not null)
            {
                Assert.Null(retention.ReleaseReceipt);
                Assert.Null(retention.CandidateCleanup);
                Assert.NotEqual(ZipRetentionState.RequiresReview, retention.State);
            }
            Assert.Null(await f.Evidence.Repository.GetArtifactAsync(new(entry.Plan.ProvisionalArtifactId!)));
        }
    }
    private static void AssertCleared(byte[]? bytes)
    { if (bytes is not null) Assert.All(bytes, value => Assert.Equal((byte)0, value)); }

    [Fact]
    public async Task Raw_snapshot_and_resume_reject_before_any_claim_or_payload_work()
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        var before = (await f.Journal.ReadAsync(f.Admission.OperationId))!;
        var decrypts = f.Runtime.Count("parent-decrypt");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Runtime.Driver.RunAsync(before));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Runtime.Driver.ResumeAsync(f.Admission.OperationId, "untrusted-worker", TimeSpan.FromMinutes(1)));
        var after = (await f.Journal.ReadAsync(f.Admission.OperationId))!;
        Assert.Equal(before.Fence, after.Fence);
        Assert.Equal(before.OwnerUntil, after.OwnerUntil);
        Assert.Equal(before.Budget, after.Budget);
        Assert.Equal(decrypts, f.Runtime.Count("parent-decrypt"));
    }

    [Fact]
    public async Task Fully_valid_attempt_materializes_one_child_and_stops_with_all_downstream_dependencies_present()
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        await using var grant = new SyntheticZipExecutionGrant(f.Admission, ZipOwnershipPurpose.InitialExecution, f.Clock);
        await using var attempt = await Claim(f, grant);
        var result = await f.Runtime.Driver.RunAsync(attempt);
        Assert.Equal(ZipPhaseIIStop.AwaitingScanner, result.Stop);
        Assert.Equal(0, result.FileOrdinal);
        Assert.Equal(ZipEntryState.Materialized, result.Parent.Entries.Single().State);
        var retained = (await f.Journal.ReadRetentionAsync(f.Admission.OperationId, 0))!;
        Assert.Equal(ZipRetentionState.Created, retained.State);
        Assert.NotNull(retained.CreateReceipt);
        await AssertNoDownstream(f, result.Parent);
        AssertCleared(f.Runtime.ParentCrypto.LastPlaintext);
        AssertCleared(f.Runtime.ChildCrypto.LastPlaintext);
        // A saved initial snapshot cannot be replayed as a new execution handoff.
        await Assert.ThrowsAsync<ZipFenceException>(() => f.Runtime.Driver.RunAsync(attempt));
        await AssertNoDownstream(f, (await f.Journal.ReadAsync(f.Admission.OperationId))!);
    }

    [Theory]
    [InlineData("ParentOpened", false)] [InlineData("ParentOpened", true)]
    [InlineData("A", false)] [InlineData("A", true)]
    [InlineData("BeforeExtraction", false)] [InlineData("BeforeExtraction", true)]
    [InlineData("Allocated", false)] [InlineData("Allocated", true)]
    [InlineData("PayloadCharged", false)] [InlineData("PayloadCharged", true)]
    [InlineData("HashObserved", false)] [InlineData("HashObserved", true)]
    [InlineData("IntegrityPersisted", false)] [InlineData("IntegrityPersisted", true)]
    public async Task Invalidation_at_protected_wait_denies_next_effect_without_review_or_refunding_work(string boundary, bool expire)
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        await using var grant = new SyntheticZipExecutionGrant(f.Admission, ZipOwnershipPurpose.InitialExecution, f.Clock);
        await using var attempt = await Claim(f, grant);
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        byte[]? child = null;
        f.Runtime.Extractor.Allocated = bytes => child = bytes;
        Task Checkpoint(string point)
        { if (point == boundary) { arrived.TrySetResult(); return release.Task; } return Task.CompletedTask; }
        f.Runtime.Driver.Checkpoint = Checkpoint;
        f.Runtime.Extractor.Checkpoint = Checkpoint;
        var run = f.Runtime.Driver.RunAsync(attempt);
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var before = (await f.Journal.ReadAsync(f.Admission.OperationId))!;
        var childEncryptions = f.Runtime.Count("child-encrypt");
        if (expire) f.Clock.Now = grant.ValidUntilUtc; else grant.Revoke();
        release.SetResult();
        await Denied(run);
        var after = (await f.Journal.ReadAsync(f.Admission.OperationId))!;
        Assert.Equal(before.Fence, after.Fence);
        Assert.Equal(before.Budget, after.Budget);
        Assert.Equal(childEncryptions, f.Runtime.Count("child-encrypt"));
        Assert.Null(await f.Journal.ReadRetentionAsync(f.Admission.OperationId, 0));
        if (boundary is "Allocated" or "PayloadCharged" or "HashObserved" or "IntegrityPersisted")
        {
            Assert.Equal(1, after.Budget.ExtractionAttempts);
            Assert.True(after.Budget.ExpandedReserved > 0);
            Assert.True(after.Budget.ProbeBytes > 0);
        }
        if (boundary == "PayloadCharged") Assert.True(after.Budget.ExpandedProduced > 0);
        await AssertNoDownstream(f, after);
        AssertCleared(child);
        AssertCleared(f.Runtime.ParentCrypto.LastPlaintext);
    }

    [Fact]
    public async Task Held_plaintext_lease_cannot_open_or_charge_next_child_after_revocation()
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        await using var grant = new SyntheticZipExecutionGrant(f.Admission, ZipOwnershipPurpose.InitialExecution, f.Clock);
        await using var attempt = await Claim(f, grant);
        var (parent, input) = await f.Runtime.ParentRetention.OpenAsync(attempt.Claimed, attempt);
        await using (input)
        {
            parent = await new ZipEntryPlanner().AdmitAsync(f.Journal, parent, input, attempt);
            var before = parent.Budget;
            var ciphertextCreates = f.Runtime.Count("child-encrypt");
            grant.Revoke();
            await Denied(f.Runtime.Extractor.MaterializeNextAsync(parent, input, attempt));
            var after = (await f.Journal.ReadAsync(f.Admission.OperationId))!;
            Assert.Equal(before, after.Budget);
            Assert.Equal(ciphertextCreates, f.Runtime.Count("child-encrypt"));
            Assert.Null(await f.Journal.ReadRetentionAsync(f.Admission.OperationId, 0));
        }
        AssertCleared(f.Runtime.ParentCrypto.LastPlaintext);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Scope_only_invalidation_during_final_async_disposal_denies_handoff_but_preserves_materialization(bool expire)
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        await using var grant = new SyntheticZipExecutionGrant(f.Admission, ZipOwnershipPurpose.InitialExecution, f.Clock) { EffectLifetime = TimeSpan.FromMinutes(1) };
        await using var attempt = await Claim(f, grant);
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gated = false;
        grant.BeforeEffectDisposal = async () =>
        {
            var current = (await f.Journal.ReadAsync(f.Admission.OperationId))!;
            if (!gated && current.Entries.Any(entry => entry.State == ZipEntryState.Materialized))
            { gated = true; arrived.TrySetResult(); await release.Task; }
        };
        var run = f.Runtime.Driver.RunAsync(attempt);
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var before = (await f.Journal.ReadAsync(f.Admission.OperationId))!;
        Assert.Equal(ZipEntryState.Materialized, before.Entries.Single().State);
        if (expire) f.Clock.Now += TimeSpan.FromMinutes(1); else grant.RevokeCurrentEffect!();
        Assert.False(grant.Revoked.IsCancellationRequested);
        release.SetResult();
        await Denied(run);
        var after = (await f.Journal.ReadAsync(f.Admission.OperationId))!;
        Assert.Equal(before.Fence, after.Fence);
        Assert.Equal(before.Budget, after.Budget);
        await AssertNoDownstream(f, after);
        AssertCleared(f.Runtime.ParentCrypto.LastPlaintext);
        // Failed scope handoff terminates this attempt even while its root is live.
        // A direct helper must not replace the invalid scope and charge another read.
        await Denied(f.Runtime.ParentRetention.OpenAsync(after, attempt));
        Assert.Equal(after.Budget, (await f.Journal.ReadAsync(f.Admission.OperationId))!.Budget);
        await using var nextInitial = new SyntheticZipExecutionGrant(f.Admission, ZipOwnershipPurpose.InitialExecution, f.Clock);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Claim(f, nextInitial));
        await using var recovery = new SyntheticZipExecutionGrant(f.Admission, ZipOwnershipPurpose.Recovery, f.Clock);
        await using var next = await Claim(f, recovery);
        var recovered = await f.Runtime.Driver.RunAsync(next);
        Assert.Equal(ZipPhaseIIStop.AwaitingScanner, recovered.Stop);
        Assert.Equal(after.Budget, recovered.Parent.Budget);
        Assert.Equal(after.Plan!.Hash, recovered.Parent.Plan!.Hash);
        Assert.Equal(after.Plan.Entries.ToArray(), recovered.Parent.Plan.Entries.ToArray());
        await AssertNoDownstream(f, recovered.Parent);
    }

    [Fact]
    public async Task Fresh_recovery_owner_fences_previous_attempt_without_payload_or_state_changes()
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        await using var initial = new SyntheticZipExecutionGrant(f.Admission, ZipOwnershipPurpose.InitialExecution, f.Clock);
        await using var old = await Claim(f, initial);
        f.Clock.Now = old.Claimed.OwnerUntil;
        await using var recovery = new SyntheticZipExecutionGrant(f.Admission, ZipOwnershipPurpose.Recovery, f.Clock);
        await using var current = await Claim(f, recovery);
        var before = (await f.Journal.ReadAsync(f.Admission.OperationId))!;
        var decryptions = f.Runtime.Count("parent-decrypt");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Runtime.Driver.RunAsync(old));
        var after = (await f.Journal.ReadAsync(f.Admission.OperationId))!;
        Assert.Equal(before.Fence, after.Fence);
        Assert.Equal(before.Budget, after.Budget);
        Assert.Equal(decryptions, f.Runtime.Count("parent-decrypt"));
        Assert.True(current.Claimed.Fence.Epoch > old.Claimed.Fence.Epoch);
    }
}
