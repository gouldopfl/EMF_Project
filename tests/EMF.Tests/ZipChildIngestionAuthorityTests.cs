using EMF.ConsoleApplication;
using EMF.Core.Contracts.Ingestion;
using EMF.Core.Contracts.Zip;
using EMF.Core.Contracts.Storage;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Services;
using EMF.Persistence.Repositories;
using EMF.Tests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace EMF.Tests;

[Collection(ZipProcessWideGateCollection.Name)]
public sealed class ZipChildIngestionAuthorityTests
{
    private static void EnableChild(ZipScannerAuthorityTests.Setup s)
        => s.Root.GuardedChildOpener = (parent, entry, gate, ct)
            => s.Fixture.Runtime.Host.OpenGuardedAsync(parent, entry, gate, ct);

    private static async Task<long> Count(ZipOwnershipFixture f, string table)
    {
        await using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = f.Evidence.DatabasePath }.ToString());
        await c.OpenAsync();
        using var q = c.CreateCommand();
        q.CommandText = table switch
        {
            "Artifacts" => "SELECT COUNT(*) FROM Artifacts",
            "Relationships" => "SELECT COUNT(*) FROM Relationships",
            "ZipExtractionAcknowledgements" => "SELECT COUNT(*) FROM ZipExtractionAcknowledgements",
            "ArtifactIngestionIntents" => "SELECT COUNT(*) FROM ArtifactIngestionIntents",
            _ => throw new ArgumentOutOfRangeException(nameof(table))
        };
        return (long)(await q.ExecuteScalarAsync())!;
    }

    private static async Task<long> DeliveredAudits(ZipOwnershipFixture f)
    {
        await using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = f.Evidence.DatabasePath }.ToString());
        await c.OpenAsync();
        using var q = c.CreateCommand();
        q.CommandText = "SELECT COUNT(*) FROM ArtifactIngestionAudit WHERE Delivered=1";
        return (long)(await q.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Authorized_scanned_child_stops_at_durable_ingested_without_ack_or_release()
    {
        await using var s = await ZipScannerAuthorityTests.Setup.Create();
        var scan = await s.Scan();
        Assert.Equal(ZipScannerStop.AwaitingChildIngestion, scan.Stop);
        EnableChild(s);
        var result = await s.Fixture.Runtime.Driver.RunChildIngestionAsync(s.Attempt);
        Assert.Equal(ZipChildIngestionStop.Ingested, result.Stop);
        Assert.Equal(ZipEntryState.Ingested, result.Parent.Entries.Single().State);
        Assert.NotNull(result.Parent.Entries.Single().Ingestion);
        Assert.Equal(-1, result.Parent.Fence.ConfirmedOrdinal);
        Assert.Equal(1, result.Parent.Budget.IngestionAttempts);
        Assert.Equal(1, await Count(s.Fixture, "Artifacts"));
        Assert.Equal(1, await Count(s.Fixture, "ArtifactIngestionIntents"));
        Assert.Equal(0, await Count(s.Fixture, "Relationships"));
        Assert.Equal(0, await Count(s.Fixture, "ZipExtractionAcknowledgements"));
        Assert.All(s.Fixture.Runtime.ChildCrypto.LastPlaintext!, b => Assert.Equal(0, b));
        Assert.Equal(ZipParentState.Processing, result.Parent.State);
    }

    [Fact]
    public async Task Revoked_execution_before_reservation_cannot_spend_budget_or_open_child()
    {
        await using var s = await ZipScannerAuthorityTests.Setup.Create();
        await s.Scan(); EnableChild(s); s.Root.Revoke();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => s.Fixture.Runtime.Driver.RunChildIngestionAsync(s.Attempt));
        var parent = (await s.Read())!;
        Assert.Equal(0, parent.Budget.IngestionAttempts);
        Assert.Equal(0, await Count(s.Fixture, "ArtifactIngestionIntents"));
    }

    [Theory]
    [InlineData(ZipChildIngestionEffect.CandidateStage)]
    [InlineData(ZipChildIngestionEffect.Encrypt)]
    [InlineData(ZipChildIngestionEffect.PhysicalCreate)]
    [InlineData(ZipChildIngestionEffect.Adoption)]
    [InlineData(ZipChildIngestionEffect.AuditWrite)]
    [InlineData(ZipChildIngestionEffect.Proof)]
    public async Task Revocation_before_next_effect_prevents_ingested_progression(ZipChildIngestionEffect effect)
    {
        await using var s = await ZipScannerAuthorityTests.Setup.Create();
        await s.Scan(); EnableChild(s);
        s.Root.OnEffectRequest = request =>
        {
            if (request is ZipChildIngestionEffectRequest { Effect: var actual } && actual == effect)
                s.Root.Revoke();
        };
        await Assert.ThrowsAnyAsync<Exception>(() => s.Fixture.Runtime.Driver.RunChildIngestionAsync(s.Attempt));
        var parent = (await s.Read())!;
        Assert.Equal(ZipEntryState.Scanned, parent.Entries.Single().State);
        Assert.Null(parent.Entries.Single().Ingestion);
        Assert.Equal(0, await Count(s.Fixture, "ZipExtractionAcknowledgements"));
        Assert.Equal(0, await Count(s.Fixture, "Relationships"));
        Assert.All(s.Fixture.Runtime.ChildCrypto.LastPlaintext!, b => Assert.Equal(0, b));
    }

    [Fact]
    public async Task Fresh_recovery_reuses_ingested_proof_without_new_reservation_or_publication()
    {
        await using var s = await ZipScannerAuthorityTests.Setup.Create();
        await s.Scan(); EnableChild(s);
        var first = await s.Fixture.Runtime.Driver.RunChildIngestionAsync(s.Attempt);
        var budget = first.Parent.Budget;
        var delivered = await DeliveredAudits(s.Fixture);
        var auditReads = 0;
        await using var root = new SyntheticZipExecutionGrant(s.Fixture.Admission, ZipOwnershipPurpose.Recovery, s.Fixture.Clock);
        root.GuardedChildOpener = (parent, entry, gate, ct)
            => s.Fixture.Runtime.Host.OpenGuardedAsync(parent, entry, gate, ct);
        root.OnEffectRequest = request =>
        {
            if (request is ZipChildIngestionEffectRequest { Effect: ZipChildIngestionEffect.AuditRead })
                auditReads++;
        };
        await using var attempt = await ZipExecutionAttempt.ClaimAsync(
            new(s.Fixture.Journal, s.Fixture.Journal, s.Fixture.Profile, s.Fixture.Clock), root,
            await s.Fixture.RequestAsync(root.Purpose), s.Fixture.Clock);
        var recovered = await s.Fixture.Runtime.Driver.RunChildIngestionAsync(attempt);
        Assert.Equal(ZipChildIngestionStop.Ingested, recovered.Stop);
        Assert.Equal(budget, recovered.Parent.Budget);
        Assert.True(auditReads > 0);
        Assert.Equal(delivered, await DeliveredAudits(s.Fixture));
        Assert.Equal(1, await Count(s.Fixture, "Artifacts"));
        Assert.Equal(0, await Count(s.Fixture, "ZipExtractionAcknowledgements"));
    }

    [Fact]
    public async Task Initial_execution_cannot_replay_durable_ingested_child()
    {
        await using var s = await ZipScannerAuthorityTests.Setup.Create();
        await s.Scan(); EnableChild(s);
        var first = await s.Fixture.Runtime.Driver.RunChildIngestionAsync(s.Attempt);
        await using var root = new SyntheticZipExecutionGrant(s.Fixture.Admission, ZipOwnershipPurpose.InitialExecution, s.Fixture.Clock);
        root.GuardedChildOpener = (parent, entry, gate, ct)
            => s.Fixture.Runtime.Host.OpenGuardedAsync(parent, entry, gate, ct);
        var request = await s.Fixture.RequestAsync(root.Purpose);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ZipExecutionAttempt.ClaimAsync(
            new(s.Fixture.Journal, s.Fixture.Journal, s.Fixture.Profile, s.Fixture.Clock), root,
            request, s.Fixture.Clock));
        var parent = (await s.Read())!;
        Assert.Equal(first.Parent.Budget, parent.Budget);
        Assert.Equal(ZipEntryState.Ingested, parent.Entries.Single().State);
        Assert.Equal(0, await Count(s.Fixture, "ZipExtractionAcknowledgements"));
    }

    [Fact]
    public async Task Recovery_from_scanned_frontier_uses_normal_pending_audit_and_then_records_proof()
    {
        await using var s = await ZipScannerAuthorityTests.Setup.Create();
        await s.Scan(); EnableChild(s);
        s.Fixture.Runtime.Ingest.Checkpoint = point => point == "CoordinatorReturned"
            ? Task.FromException(new InvalidOperationException("Synthetic process stop before ZIP proof."))
            : Task.CompletedTask;
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.Fixture.Runtime.Driver.RunChildIngestionAsync(s.Attempt));
        var interrupted = (await s.Read())!;
        Assert.Equal(ZipEntryState.Scanned, interrupted.Entries.Single().State);
        Assert.Equal(1, interrupted.Budget.IngestionAttempts);
        Assert.True(await DeliveredAudits(s.Fixture) > 0);
        s.Fixture.Runtime.Ingest.Checkpoint = null;
        var pendingWrites = 0;
        await using var root = new SyntheticZipExecutionGrant(s.Fixture.Admission, ZipOwnershipPurpose.Recovery, s.Fixture.Clock);
        root.GuardedChildOpener = (parent, entry, gate, ct)
            => s.Fixture.Runtime.Host.OpenGuardedAsync(parent, entry, gate, ct);
        root.OnEffectRequest = request =>
        {
            if (request is ZipChildIngestionEffectRequest { Effect: ZipChildIngestionEffect.AuditWrite })
                pendingWrites++;
        };
        await using var attempt = await ZipExecutionAttempt.ClaimAsync(
            new(s.Fixture.Journal, s.Fixture.Journal, s.Fixture.Profile, s.Fixture.Clock), root,
            await s.Fixture.RequestAsync(root.Purpose), s.Fixture.Clock);
        var recovered = await s.Fixture.Runtime.Driver.RunChildIngestionAsync(attempt);
        Assert.Equal(ZipChildIngestionStop.Ingested, recovered.Stop);
        Assert.True(pendingWrites > 0);
        Assert.Equal(1, recovered.Parent.Budget.IngestionAttempts);
        Assert.Equal(1, await Count(s.Fixture, "Artifacts"));
        Assert.Equal(0, await Count(s.Fixture, "ZipExtractionAcknowledgements"));
    }

    [Fact]
    public async Task Delegated_audit_reads_and_acknowledgement_reject_foreign_child_operation()
    {
        await using var s = await ZipScannerAuthorityTests.Setup.Create();
        await s.Scan(); EnableChild(s);
        var completed = await s.Fixture.Runtime.Driver.RunChildIngestionAsync(s.Attempt);
        var entry = completed.Parent.Entries.Single();
        var fenced = new ZipFencedIngestionPersistence(s.Fixture.Evidence.DatabasePath,
            completed.Parent.Fence, entry, s.Fixture.Clock);
        var own = new ArtifactContentOperationId(entry.Plan.ChildOperationId!);
        var foreign = ArtifactContentOperationId.New();
        var obligation = (await fenced.ReadAuditObligationsAsync(own)).First();
        var delivered = await DeliveredAudits(s.Fixture);
        var gate = new ZipChildIngestionEffectGate(s.Attempt, s.Fixture.Journal, () => completed.Parent, 0);
        var delegated = new ZipGuardedIngestionPersistence(fenced, gate);

        await Assert.ThrowsAsync<InvalidDataException>(() => delegated.ReadAuditObligationsAsync(foreign));
        await Assert.ThrowsAsync<InvalidDataException>(() => delegated.ReadReviewAuditObligationAsync(foreign));
        await Assert.ThrowsAsync<InvalidDataException>(() => delegated.AcknowledgeAuditAsync(
            obligation with { OperationId = foreign }));
        Assert.Equal(delivered, await DeliveredAudits(s.Fixture));
        Assert.Equal(ZipEntryState.Ingested, (await s.Read())!.Entries.Single().State);
    }

    [Fact]
    public async Task Reservation_lookup_requires_current_parent_fence_and_exact_ingestion_occurrence()
    {
        await using var s = await ZipScannerAuthorityTests.Setup.Create();
        var scanned = (await s.Scan()).Parent;
        Assert.False(await s.Fixture.Journal.HasIngestionReservationAsync(scanned.Fence, 0));
        EnableChild(s);
        var completed = await s.Fixture.Runtime.Driver.RunChildIngestionAsync(s.Attempt);
        Assert.True(await s.Fixture.Journal.HasIngestionReservationAsync(completed.Parent.Fence, 0));
        await Assert.ThrowsAsync<ZipFenceException>(() =>
            s.Fixture.Journal.HasIngestionReservationAsync(scanned.Fence, 0));
        await Assert.ThrowsAsync<ZipFenceException>(() =>
            s.Fixture.Journal.HasIngestionReservationAsync(completed.Parent.Fence, 1));
        await Assert.ThrowsAsync<ZipFenceException>(() =>
            s.Fixture.Journal.HasIngestionReservationAsync(
                completed.Parent.Fence with { OperationId = "unrelated-parent" }, 0));
        Assert.Equal(1, completed.Parent.Budget.IngestionAttempts);
        Assert.Equal(0, await Count(s.Fixture, "ZipExtractionAcknowledgements"));
    }
}
