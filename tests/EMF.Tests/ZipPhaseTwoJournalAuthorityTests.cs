using EMF.Core.Contracts.Zip;
using EMF.Core.Contracts.Storage;
using EMF.Tests.TestInfrastructure;
using EMF.Persistence.Repositories;

namespace EMF.Tests;

[Collection(ZipProcessWideGateCollection.Name)]
public sealed class ZipPhaseTwoJournalAuthorityTests
{
    private sealed class Guard : IZipEffectBoundaryGuard, IDisposable
    {
        private readonly CancellationTokenSource _invalidated = new();
        internal Action<int>? OnValidation;
        internal Action? AfterAcceptance;
        internal int Validations;
        internal int Acceptances;
        internal bool NullAcceptance;
        public CancellationToken Invalidated => _invalidated.Token;
        internal void Revoke() => _invalidated.Cancel();
        public ValueTask RevalidateAsync(CancellationToken ct = default)
        {
            Validations++;
            OnValidation?.Invoke(Validations);
            ct.ThrowIfCancellationRequested();
            Invalidated.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
        public IDisposable Accept(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested(); Invalidated.ThrowIfCancellationRequested();
            if (NullAcceptance) return null!;
            Acceptances++;
            return new Acceptance(() => AfterAcceptance?.Invoke());
        }
        private sealed class Acceptance(Action dispose) : IDisposable { public void Dispose() => dispose(); }
        public void Dispose() => _invalidated.Dispose();
    }

    [Fact]
    public async Task Revocation_before_entry_performs_no_reservation()
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        var parent = await f.Journal.ClaimOwnershipAsync(await f.RequestAsync(ZipOwnershipPurpose.InitialExecution));
        using var guard = new Guard(); guard.Revoke();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Journal.ReserveAsync(parent.Fence,
            new("denied-preflight", ZipWorkKind.Preflight, null, parent.Binding.Input.Length + 65539), guard));
        var actual = (await f.Journal.ReadAsync(parent.Binding.OperationId))!;
        Assert.Equal(parent.Fence, actual.Fence); Assert.Equal(parent.Budget, actual.Budget);
        Assert.Equal(0, guard.Acceptances);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Revocation_at_final_acceptance_rolls_back_reservation_or_plan(bool plan)
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        var parent = await f.Journal.ClaimOwnershipAsync(await f.RequestAsync(ZipOwnershipPurpose.InitialExecution));
        if (plan) parent = await f.Journal.ReserveAsync(parent.Fence,
            new("existing-preflight", ZipWorkKind.Preflight, null, parent.Binding.Input.Length + 65539));
        using var guard = new Guard();
        guard.OnValidation = count => { if (count == 3) guard.Revoke(); };
        Task Change() => plan
            ? f.Journal.AdmitPlanAsync(parent.Fence, EmptyPlan(), guard)
            : f.Journal.ReserveAsync(parent.Fence, new("denied-preflight", ZipWorkKind.Preflight, null,
                parent.Binding.Input.Length + 65539), guard);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(Change);
        var actual = (await f.Journal.ReadAsync(parent.Binding.OperationId))!;
        Assert.Equal(3, guard.Validations); Assert.Equal(0, guard.Acceptances);
        Assert.Equal(parent.Fence, actual.Fence); Assert.Equal(parent.Budget, actual.Budget);
        Assert.Null(actual.Plan);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ownership_expiry_during_final_authority_revalidation_rolls_back(bool plan)
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        var parent = await f.Journal.ClaimOwnershipAsync(await f.RequestAsync(ZipOwnershipPurpose.InitialExecution));
        if (plan) parent = await f.Journal.ReserveAsync(parent.Fence,
            new("existing-preflight", ZipWorkKind.Preflight, null, parent.Binding.Input.Length + 65539));
        using var guard = new Guard();
        guard.OnValidation = count => { if (count == 3) f.Clock.Now = parent.OwnerUntil; };
        Task Change() => plan
            ? f.Journal.AdmitPlanAsync(parent.Fence, EmptyPlan(), guard)
            : f.Journal.ReserveAsync(parent.Fence, new("expired-preflight", ZipWorkKind.Preflight, null,
                parent.Binding.Input.Length + 65539), guard);
        await Assert.ThrowsAsync<ZipFenceException>(Change);
        var actual = (await f.Journal.ReadAsync(parent.Binding.OperationId))!;
        Assert.Equal(1, guard.Acceptances);
        Assert.Equal(parent.Fence, actual.Fence); Assert.Equal(parent.Budget, actual.Budget);
        Assert.Null(actual.Plan);
    }

    [Fact]
    public async Task Committed_charge_survives_invalidation_and_is_not_refunded()
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        var parent = await f.Journal.ClaimOwnershipAsync(await f.RequestAsync(ZipOwnershipPurpose.InitialExecution));
        using var guard = new Guard(); guard.AfterAcceptance = guard.Revoke;
        var committed = await f.Journal.ChargeParentReadAsync(parent.Fence, guard);
        Assert.True(guard.Invalidated.IsCancellationRequested);
        var actual = (await f.Journal.ReadAsync(parent.Binding.OperationId))!;
        Assert.Equal(committed.Fence, actual.Fence);
        Assert.Equal(parent.Binding.Input.Length, actual.Budget.ParentReadBytes);
        Assert.Equal(parent.Fence.Revision + 1, actual.Fence.Revision);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Journal.ChargeParentReadAsync(actual.Fence, guard));
        Assert.Equal(actual.Budget, (await f.Journal.ReadAsync(parent.Binding.OperationId))!.Budget);
    }

    [Fact]
    public async Task Current_guard_cannot_manufacture_plan_eligibility_or_accept_stale_fence()
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        var parent = await f.Journal.ClaimOwnershipAsync(await f.RequestAsync(ZipOwnershipPurpose.InitialExecution));
        using var guard = new Guard();
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Journal.AdmitPlanAsync(parent.Fence, EmptyPlan(), guard));
        Assert.Equal(0, guard.Acceptances);
        var reserved = await f.Journal.ReserveAsync(parent.Fence,
            new("preflight", ZipWorkKind.Preflight, null, parent.Binding.Input.Length + 65539), guard);
        await Assert.ThrowsAsync<ZipFenceException>(() => f.Journal.AdmitPlanAsync(parent.Fence, EmptyPlan(), guard));
        Assert.Equal(reserved.Fence, (await f.Journal.ReadAsync(parent.Binding.OperationId))!.Fence);
        Assert.Null((await f.Journal.ReadAsync(parent.Binding.OperationId))!.Plan);
    }

    [Fact]
    public async Task Guarded_scanner_reservation_requires_atomic_policy_binding()
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        var parent = await f.Journal.ClaimOwnershipAsync(await f.RequestAsync(ZipOwnershipPurpose.InitialExecution));
        using var guard = new Guard();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Journal.ReserveAsync(parent.Fence,
            new("downstream", ZipWorkKind.Scanner, 0, 0), guard));
        Assert.Equal(0, guard.Acceptances);
        var actual = (await f.Journal.ReadAsync(parent.Binding.OperationId))!;
        Assert.Equal(parent.Fence, actual.Fence);
        Assert.Equal(parent.Budget, actual.Budget);
        await AssertNoReservationsAsync(f, parent.Fence.OperationId);
    }

    [Fact]
    public async Task Guarded_ingestion_reservation_requires_current_admitted_occurrence()
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        var parent = await f.Journal.ClaimOwnershipAsync(await f.RequestAsync(ZipOwnershipPurpose.InitialExecution));
        using var guard = new Guard();
        await Assert.ThrowsAsync<ZipFenceException>(() => f.Journal.ReserveAsync(parent.Fence,
            new("downstream", ZipWorkKind.Ingestion, 0, 0), guard));
        Assert.Equal(0, guard.Acceptances);
        var actual = (await f.Journal.ReadAsync(parent.Binding.OperationId))!;
        Assert.Equal(parent.Fence, actual.Fence);
        Assert.Equal(parent.Budget, actual.Budget);
        await AssertNoReservationsAsync(f, parent.Fence.OperationId);
    }

    private static async Task AssertNoReservationsAsync(ZipOwnershipFixture f, string parentOperationId)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={f.Evidence.DatabasePath}");
        await connection.OpenAsync();
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT COUNT(*) FROM ZipExtractionReservations WHERE ParentOperationId=$op";
        query.Parameters.AddWithValue("$op", parentOperationId);
        Assert.Equal(0L, (long)(await query.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Initial_retention_reservation_is_rolled_back_when_authority_ends_before_commit()
    {
        await using var f = await ZipAdmissionFixture.CreateAsync();
        var admission = await SourceBoundAsync(f);
        var operation = admission.OperationId;
        using var guard = new Guard();
        guard.OnValidation = count => { if (count == 3) guard.Revoke(); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Journal.ReserveParentRetentionAsync(
            operation, admission.Binding.ParentArtifactId, admission.Source!, f.Profile.Fingerprint,
            f.Profile.ParentNamespaceId, guard));
        Assert.Null(await f.Journal.ReadParentRetentionAsync(operation));
        Assert.Null(await f.Journal.ReadAsync(operation));
        Assert.Equal(0, guard.Acceptances);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Initial_retention_preparation_or_candidate_binding_requires_current_acceptance(bool bind)
    {
        await using var f = await ZipAdmissionFixture.CreateAsync();
        var admission = await SourceBoundAsync(f);
        var retention = await f.Journal.ReserveParentRetentionAsync(admission.OperationId,
            admission.Binding.ParentArtifactId, admission.Source!, f.Profile.Fingerprint, f.Profile.ParentNamespaceId);
        if (bind) retention = await f.Journal.BeginParentPreparationAsync(retention);
        using var guard = new Guard();
        guard.OnValidation = count => { if (count == 3) guard.Revoke(); };
        Task Change() => bind
            ? f.Journal.BindParentCandidateAsync(retention, new string('A', 64), guard)
            : f.Journal.BeginParentPreparationAsync(retention, guard);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(Change);
        Assert.Equal(retention, await f.Journal.ReadParentRetentionAsync(retention.Identity.ParentOperationId));
        Assert.Null(await f.Journal.ReadAsync(retention.Identity.ParentOperationId));
    }

    [Fact]
    public async Task Source_bound_evidence_is_not_appended_after_scope_invalidation()
    {
        await using var f = await ZipAdmissionFixture.CreateAsync();
        var admission = await f.Journal.ResolveOrReserveAdmissionAsync(f.Key, f.Binding);
        admission = await f.Journal.AppendAdmissionEventAsync(admission, f.Approval(admission));
        var source = new ZipRetainedBinding(f.Binding.SourceContentId, f.Binding.SourceRevision, new string('A', 64), 1);
        using var guard = new Guard(); guard.OnValidation = count => { if (count == 3) guard.Revoke(); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Journal.AppendAdmissionEventAsync(admission,
            new("source", ZipAdmissionEventKind.SourceBound, f.Binding.OriginalActorId, f.Time.Now, Source: source), guard));
        Assert.Equal(admission, await f.Journal.ReadAdmissionAsync(admission.OperationId));
        Assert.Equal(0, guard.Acceptances);
    }

    [Theory]
    [InlineData(ZipAdmissionEventKind.Approved)]
    [InlineData(ZipAdmissionEventKind.Completed)]
    [InlineData(ZipAdmissionEventKind.RequiresReview)]
    public async Task Initial_retention_guard_cannot_append_other_admission_effects(ZipAdmissionEventKind kind)
    {
        await using var f = await ZipAdmissionFixture.CreateAsync();
        var admission = await f.Journal.ResolveOrReserveAdmissionAsync(f.Key, f.Binding);
        using var guard = new Guard();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Journal.AppendAdmissionEventAsync(admission,
            new("other-effect", kind, f.Binding.OriginalActorId, f.Time.Now), guard));
        Assert.Equal(admission, await f.Journal.ReadAdmissionAsync(admission.OperationId));
        Assert.Equal(0, guard.Acceptances);
    }

    private class RawPrepared : IPreparedArtifactContentMutation
    {
        internal int Executions;
        public Task<ArtifactContentMutationResult> ExecuteAsync(CancellationToken ct = default)
        {
            Executions++;
            throw new InvalidOperationException("A rejected preparation must never execute.");
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class OtherGuardPrepared(IZipEffectBoundaryGuard guard) : RawPrepared, IZipGuardedPreparedContentMutation
    {
        public IZipEffectBoundaryGuard BoundaryGuard => guard;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Initial_retention_promotion_refuses_raw_or_differently_guarded_preparation(bool otherGuard)
    {
        await using var f = await ZipAdmissionFixture.CreateAsync();
        var admission = await SourceBoundAsync(f);
        var retention = await f.Journal.ReserveParentRetentionAsync(admission.OperationId, f.Binding.ParentArtifactId,
            admission.Source!, f.Profile.Fingerprint, f.Profile.ParentNamespaceId);
        retention = await f.Journal.BeginParentPreparationAsync(retention);
        retention = await f.Journal.BindParentCandidateAsync(retention, new string('B', 64));
        using var guard = new Guard(); using var other = new Guard();
        await using RawPrepared prepared = otherGuard ? new OtherGuardPrepared(other) : new RawPrepared();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Journal.PromoteParentRetentionAsync(retention,
            prepared, null, guard));
        Assert.Equal(0, prepared.Executions);
        Assert.Equal(retention, await f.Journal.ReadParentRetentionAsync(retention.Identity.ParentOperationId));
    }

    [Fact]
    public async Task Current_live_scope_cannot_bypass_evidence_expiry_during_final_validation()
    {
        await using var f = await ZipAdmissionFixture.CreateAsync();
        var admission = await f.Journal.ResolveOrReserveAdmissionAsync(f.Key, f.Binding);
        admission = await f.Journal.AppendAdmissionEventAsync(admission, f.Approval(admission));
        var source = new ZipRetainedBinding(f.Binding.SourceContentId, f.Binding.SourceRevision, new string('A', 64), 1);
        using var guard = new Guard();
        guard.OnValidation = count => { if (count == 3) f.Time.Now = admission.LatestAuthorityEvidence!.ExpiresUtc; };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Journal.AppendAdmissionEventAsync(admission,
            new("source", ZipAdmissionEventKind.SourceBound, f.Binding.OriginalActorId, f.Time.Now, Source: source), guard));
        Assert.Equal(admission, await f.Journal.ReadAdmissionAsync(admission.OperationId));
        Assert.Equal(1, guard.Acceptances);
    }

    [Fact]
    public async Task Two_real_connections_cannot_commit_the_same_guarded_plan_projection_twice()
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        var parent = await f.Journal.ClaimOwnershipAsync(await f.RequestAsync(ZipOwnershipPurpose.InitialExecution));
        parent = await f.Journal.ReserveAsync(parent.Fence,
            new("preflight", ZipWorkKind.Preflight, null, parent.Binding.Input.Length + 65539));
        var plan = EmptyPlan();
        using var firstGuard = new Guard(); using var secondGuard = new Guard();
        using var start = new Barrier(2);
        async Task<bool> Commit(Guard guard)
        {
            start.SignalAndWait();
            var journal = new SqliteZipExtractionJournal(f.Evidence.DatabasePath, f.Clock);
            try { await journal.AdmitPlanAsync(parent.Fence, plan, guard); return true; }
            catch (ZipFenceException) { return false; }
        }
        var results = await Task.WhenAll(Task.Run(() => Commit(firstGuard)), Task.Run(() => Commit(secondGuard)));
        Assert.Equal(1, results.Count(success => success));
        Assert.Equal(1, firstGuard.Acceptances + secondGuard.Acceptances);
        var actual = (await f.Journal.ReadAsync(parent.Binding.OperationId))!;
        Assert.Equal(parent.Fence.Revision + 1, actual.Fence.Revision);
        Assert.Equal(plan.Hash, actual.Fence.PlanHash);
        Assert.Equal(parent.Budget, actual.Budget);
    }

    [Fact]
    public async Task Malformed_guard_without_acceptance_cannot_commit_parent_read_charge()
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        var parent = await f.Journal.ClaimOwnershipAsync(await f.RequestAsync(ZipOwnershipPurpose.InitialExecution));
        using var guard = new Guard { NullAcceptance = true };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Journal.ChargeParentReadAsync(parent.Fence, guard));
        var actual = (await f.Journal.ReadAsync(parent.Binding.OperationId))!;
        Assert.Equal(parent.Fence, actual.Fence); Assert.Equal(parent.Budget, actual.Budget);
        Assert.Equal(3, guard.Validations); Assert.Equal(0, guard.Acceptances);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Null_admission_arguments_are_rejected_without_calling_authority(bool nullEvidence)
    {
        await using var f = await ZipAdmissionFixture.CreateAsync();
        var admission = await f.Journal.ResolveOrReserveAdmissionAsync(f.Key, f.Binding);
        using var guard = new Guard();
        var error = await Assert.ThrowsAsync<ArgumentNullException>(() => f.Journal.AppendAdmissionEventAsync(
            nullEvidence ? admission : null!, nullEvidence ? null! : f.Approval(admission), guard));
        Assert.Equal(nullEvidence ? "evidence" : "expected", error.ParamName);
        Assert.Equal(0, guard.Validations); Assert.Equal(0, guard.Acceptances);
        Assert.Equal(admission, await f.Journal.ReadAdmissionAsync(admission.OperationId));
    }

    private static async Task<ZipParentAdmission> SourceBoundAsync(ZipAdmissionFixture f)
    {
        var admission = await f.Journal.ResolveOrReserveAdmissionAsync(f.Key, f.Binding);
        admission = await f.Journal.AppendAdmissionEventAsync(admission, f.Approval(admission));
        return await f.Journal.AppendAdmissionEventAsync(admission, new("source", ZipAdmissionEventKind.SourceBound,
            f.Binding.OriginalActorId, f.Time.Now, Source: new(f.Binding.SourceContentId, f.Binding.SourceRevision, new string('A', 64), 1)));
    }

    [Theory]
    [InlineData(ZipAdmissionEventKind.Revoked)]
    [InlineData(ZipAdmissionEventKind.RequiresReview)]
    public async Task Current_stopped_admission_vetoes_guarded_plan_even_with_valid_fence_and_live_guard(ZipAdmissionEventKind stop)
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        var parent = await f.Journal.ClaimOwnershipAsync(await f.RequestAsync(ZipOwnershipPurpose.InitialExecution));
        parent = await f.Journal.ReserveAsync(parent.Fence,
            new("preflight", ZipWorkKind.Preflight, null, parent.Binding.Input.Length + 65539));
        await f.Journal.AppendAdmissionEventAsync(f.Admission, new("durable-stop", stop,
            f.Admission.Binding.OriginalActorId, f.Clock.Now, SafeReason: "DurableEligibilityStopped"));
        using var guard = new Guard();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Journal.AdmitPlanAsync(parent.Fence, EmptyPlan(), guard));
        var actual = (await f.Journal.ReadAsync(parent.Binding.OperationId))!;
        Assert.Equal(parent.Fence, actual.Fence); Assert.Equal(parent.Budget, actual.Budget); Assert.Null(actual.Plan);
        Assert.False(guard.Invalidated.IsCancellationRequested); Assert.Equal(0, guard.Acceptances);
    }

    [Theory]
    [InlineData(ZipAdmissionEventKind.Revoked, false)]
    [InlineData(ZipAdmissionEventKind.RequiresReview, false)]
    [InlineData(ZipAdmissionEventKind.Revoked, true)]
    [InlineData(ZipAdmissionEventKind.RequiresReview, true)]
    public async Task Initial_retention_current_stop_vetoes_reservation_or_preparation(ZipAdmissionEventKind stop, bool preparing)
    {
        await using var f = await ZipAdmissionFixture.CreateAsync();
        var admission = await SourceBoundAsync(f);
        var retained = preparing ? await f.Journal.ReserveParentRetentionAsync(admission.OperationId,
            f.Binding.ParentArtifactId, admission.Source!, f.Profile.Fingerprint, f.Profile.ParentNamespaceId) : null;
        await f.Journal.AppendAdmissionEventAsync(admission, new("durable-stop", stop,
            f.Binding.OriginalActorId, f.Time.Now, SafeReason: "DurableEligibilityStopped"));
        using var guard = new Guard();
        Task Change() => preparing ? f.Journal.BeginParentPreparationAsync(retained!, guard)
            : f.Journal.ReserveParentRetentionAsync(admission.OperationId, f.Binding.ParentArtifactId,
                admission.Source!, f.Profile.Fingerprint, f.Profile.ParentNamespaceId, guard);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(Change);
        Assert.Equal(retained, await f.Journal.ReadParentRetentionAsync(admission.OperationId));
        Assert.False(guard.Invalidated.IsCancellationRequested); Assert.Equal(0, guard.Acceptances);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("profile")]
    [InlineData("namespace")]
    [InlineData("artifact")]
    public async Task Initial_retention_requires_exact_current_admission_binding(string changed)
    {
        await using var f = await ZipAdmissionFixture.CreateAsync();
        var admission = await SourceBoundAsync(f); using var guard = new Guard();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Journal.ReserveParentRetentionAsync(admission.OperationId,
            changed == "artifact" ? "different-parent" : f.Binding.ParentArtifactId,
            changed == "source" ? admission.Source! with { Revision = "different-revision" } : admission.Source!,
            changed == "profile" ? new string('C', 64) : f.Profile.Fingerprint,
            changed == "namespace" ? new string('C', 64) : f.Profile.ParentNamespaceId, guard));
        Assert.Null(await f.Journal.ReadParentRetentionAsync(admission.OperationId)); Assert.Equal(0, guard.Acceptances);
    }

    [Fact]
    public async Task Approval_pending_or_approved_without_SourceBound_cannot_create_initial_retention()
    {
        await using var f = await ZipAdmissionFixture.CreateAsync();
        var admission = await f.Journal.ResolveOrReserveAdmissionAsync(f.Key, f.Binding);
        using var guard = new Guard();
        async Task Reserve() => await f.Journal.ReserveParentRetentionAsync(admission.OperationId,
            f.Binding.ParentArtifactId, new(f.Binding.SourceContentId, f.Binding.SourceRevision, new string('A', 64), 1),
            f.Profile.Fingerprint, f.Profile.ParentNamespaceId, guard);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(Reserve);
        admission = await f.Journal.AppendAdmissionEventAsync(admission, f.Approval(admission));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(Reserve);
        Assert.Null(await f.Journal.ReadParentRetentionAsync(admission.OperationId)); Assert.Equal(0, guard.Acceptances);
    }

    [Fact]
    public async Task Current_admission_evidence_expiry_during_final_guard_check_vetoes_execution_commit()
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        var current = await f.Journal.AppendAdmissionEventAsync(f.Admission, new("short-current-evidence",
            ZipAdmissionEventKind.AuthorityValidated, "synthetic-current-actor", f.Clock.Now,
            f.Admission.Approval! with { DecisionId = "short", IssuedUtc = f.Clock.Now, ExpiresUtc = f.Clock.Now.AddMinutes(1) }));
        var request = (await f.RequestAsync(ZipOwnershipPurpose.InitialExecution)) with { ExpectedAdmission = current };
        var parent = await f.Journal.ClaimOwnershipAsync(request);
        using var guard = new Guard();
        guard.OnValidation = count => { if (count == 3) f.Clock.Now = current.LatestAuthorityEvidence!.ExpiresUtc; };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Journal.ChargeParentReadAsync(parent.Fence, guard));
        var actual = (await f.Journal.ReadAsync(parent.Binding.OperationId))!;
        Assert.Equal(parent.Fence, actual.Fence); Assert.Equal(parent.Budget, actual.Budget);
        Assert.Equal(1, guard.Acceptances);
    }

    private static ZipPlan EmptyPlan()
    {
        const string receipt = "{\"PreflightVersion\":1}";
        return new(ZipPlanBinding.Compute(receipt, []), receipt, []);
    }
}
