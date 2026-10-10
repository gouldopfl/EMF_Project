using EMF.ConsoleApplication;
using EMF.Core.Contracts.Zip;
using EMF.Core.Models;
using EMF.Persistence.Repositories;
using EMF.Tests.TestInfrastructure;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class ZipParentAdmissionJournalTests
{
    [Fact]
    public async Task Reservation_survives_restart_without_approval_or_payload_side_effects()
    {
        await using var f = await ZipAdmissionFixture.CreateAsync();
        var p = await f.Journal.ResolveOrReserveAdmissionAsync(f.Key, f.Binding);
        Assert.Equal(ZipAdmissionState.ApprovalPending, p.State); Assert.Null(p.Approval);
        Assert.Equal(p, await f.Journal.ResolveOrReserveAdmissionAsync(f.Key, f.Binding));
        Assert.Equal(p, await f.Journal.ReadAdmissionByKeyAsync(f.Key));
        Assert.Equal(p, await f.Journal.ReadAdmissionAsync(p.OperationId));
        Assert.Equal(0, await f.ScalarAsync("SELECT COUNT(*) FROM ZipParentAdmissionEvents"));
        Assert.Equal(0, await f.ScalarAsync("SELECT COUNT(*) FROM ZipExtractionParents"));
        Assert.Equal(0, await f.ScalarAsync("SELECT COUNT(*) FROM ZipExtractionRetentions"));
        Assert.Equal(0, await f.ScalarAsync("SELECT COUNT(*) FROM Artifacts"));
    }

    [Fact]
    public async Task Independent_concurrent_resolvers_reserve_one_original_operation()
    {
        await using var f = await ZipAdmissionFixture.CreateAsync();
        var requests = Enumerable.Range(0, 8).Select(_ => Task.Run(() => f.Journal.ResolveOrReserveAdmissionAsync(f.Key, f.Binding)));
        var results = await Task.WhenAll(requests);
        Assert.Single(results.Select(p => p.OperationId).Distinct());
        Assert.Equal(1, await f.ScalarAsync("SELECT COUNT(*) FROM ZipParentAdmissions"));
    }

    [Theory]
    [InlineData("revision")] [InlineData("source")] [InlineData("artifact")] [InlineData("actor")]
    [InlineData("operation")] [InlineData("workflow")] [InlineData("profile")] [InlineData("namespace")]
    public async Task Same_request_cannot_silently_switch_binding(string part)
    {
        await using var f = await ZipAdmissionFixture.CreateAsync();
        var p = await f.Journal.ResolveOrReserveAdmissionAsync(f.Key, f.Binding);
        var binding = part switch
        {
            "revision" => f.Binding with { SourceRevision = "other-revision" },
            "source" => f.Binding with { SourceContentId = "other-content" },
            "artifact" => f.Binding with { ParentArtifactId = "other-artifact" },
            "actor" => f.Binding with { OriginalActorId = "other-actor" },
            "operation" => f.Binding with { AuthorizedOperationId = "other-authorized-operation" },
            "workflow" => f.Binding with { WorkflowOperationId = "other-workflow-operation" },
            "namespace" => f.Binding with { ParentNamespaceId = new string('D', 64) },
            _ => f.Binding with { ProfileJson = "{}", ProfileHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("{}"u8)) }
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Journal.ResolveOrReserveAdmissionAsync(f.Key, binding));
        Assert.Equal(p, await f.Journal.ReadAdmissionByKeyAsync(f.Key));
        Assert.Equal(1, await f.ScalarAsync("SELECT COUNT(*) FROM ZipParentAdmissions"));
    }

    [Fact]
    public async Task Approval_is_explicit_immutable_and_idempotent_even_after_recording_crash()
    {
        await using var f = await ZipAdmissionFixture.CreateAsync();
        var pending = await f.Journal.ResolveOrReserveAdmissionAsync(f.Key, f.Binding);
        var approval = f.Approval(pending);
        var approved = await f.Journal.AppendAdmissionEventAsync(pending, approval);
        Assert.Equal(ZipAdmissionState.Approved, approved.State); Assert.Equal(1, approved.Revision);
        Assert.Equal(approved, await f.Journal.AppendAdmissionEventAsync(pending, approval));
        Assert.Equal(approved, await f.Journal.ResolveOrReserveAdmissionAsync(f.Key, f.Binding));
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Journal.AppendAdmissionEventAsync(pending,
            approval with { Approval = approval.Approval! with { AuthorityRevision = "changed" } }));
        Assert.Equal(1, await f.ScalarAsync("SELECT COUNT(*) FROM ZipParentAdmissionEvents"));
    }

    [Theory]
    [InlineData("operation")] [InlineData("binding")] [InlineData("issuer")] [InlineData("actor")]
    [InlineData("permission")] [InlineData("expired")]
    public async Task Approval_must_bind_original_request_and_actual_grant(string part)
    {
        await using var f = await ZipAdmissionFixture.CreateAsync();
        var pending = await f.Journal.ResolveOrReserveAdmissionAsync(f.Key, f.Binding);
        var e = f.Approval(pending); var a = e.Approval!;
        e = part switch
        {
            "operation" => e with { Approval = a with { ParentOperationId = "other" } },
            "binding" => e with { Approval = a with { BindingHash = new string('D', 64) } },
            "issuer" => e with { Approval = a with { IssuerId = "other" } },
            "actor" => e with { ExecutingActorId = "console-admin" },
            "permission" => e with { Approval = a with { Capabilities = ZipAuthorityCapabilities.None } },
            _ => e with { Approval = a with { ExpiresUtc = f.Time.Now } }
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Journal.AppendAdmissionEventAsync(pending, e));
        Assert.Equal(pending, await f.Journal.ReadAdmissionAsync(pending.OperationId));
    }

    [Fact]
    public async Task Recovery_revalidation_preserves_original_actor_and_grant_and_rejects_policy_change()
    {
        await using var f = await ZipAdmissionFixture.CreateAsync();
        var p = await f.Journal.ResolveOrReserveAdmissionAsync(f.Key, f.Binding);
        p = await f.Journal.AppendAdmissionEventAsync(p, f.Approval(p)); var original = p.Approval;
        f.Time.Now += TimeSpan.FromHours(2);
        var e = new ZipAdmissionEvent("recovery-check", ZipAdmissionEventKind.AuthorityValidated, "synthetic-recovery-actor", f.Time.Now,
            original! with { DecisionId = "recovery-decision", IssuedUtc = f.Time.Now, ExpiresUtc = f.Time.Now.AddHours(1) });
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Journal.AppendAdmissionEventAsync(p,
            e with { Approval = e.Approval! with { PolicyVersion = "different" } }));
        p = await f.Journal.AppendAdmissionEventAsync(p, e);
        Assert.Equal(original, p.Approval); Assert.Equal(f.Binding.OriginalActorId, p.Binding.OriginalActorId);
        Assert.Equal(e.Approval, p.LatestAuthorityEvidence); Assert.Equal(p, await f.Journal.ReadAdmissionAsync(p.OperationId));
    }

    [Theory]
    [InlineData(ZipAdmissionEventKind.Denied)] [InlineData(ZipAdmissionEventKind.RequiresReview)] [InlineData(ZipAdmissionEventKind.Revoked)]
    public async Task Stopped_admission_cannot_be_approved_or_replaced(ZipAdmissionEventKind kind)
    {
        await using var f = await ZipAdmissionFixture.CreateAsync();
        var pending = await f.Journal.ResolveOrReserveAdmissionAsync(f.Key, f.Binding); var p = pending;
        if (kind == ZipAdmissionEventKind.Revoked) p = await f.Journal.AppendAdmissionEventAsync(p, f.Approval(p));
        p = await f.Journal.AppendAdmissionEventAsync(p, new("stop-event", kind, "synthetic-test-actor", f.Time.Now, SafeReason: "AuthorityStopped"));
        await Assert.ThrowsAsync<ZipFenceException>(() => f.Journal.AppendAdmissionEventAsync(pending, f.Approval(pending) with { EventId = "late-approval" }));
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Journal.AppendAdmissionEventAsync(p, f.Approval(p) with { EventId = "reopen" }));
        Assert.Equal(p, await f.Journal.ResolveOrReserveAdmissionAsync(f.Key, f.Binding));
    }

    [Fact]
    public async Task Source_binding_is_once_only_exact_and_requires_unexpired_approval()
    {
        await using var f = await ZipAdmissionFixture.CreateAsync();
        var p = await f.Journal.ResolveOrReserveAdmissionAsync(f.Key, f.Binding);
        var source = new ZipRetainedBinding(f.Binding.SourceContentId, f.Binding.SourceRevision, new string('D', 64), 12);
        var e = new ZipAdmissionEvent("source-event", ZipAdmissionEventKind.SourceBound, f.Binding.OriginalActorId, f.Time.Now, Source: source);
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Journal.AppendAdmissionEventAsync(p, e));
        p = await f.Journal.AppendAdmissionEventAsync(p, f.Approval(p));
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Journal.AppendAdmissionEventAsync(p, e with { Source = source with { Revision = "newer" } }));
        f.Time.Now += TimeSpan.FromHours(2);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Journal.AppendAdmissionEventAsync(p, e));
        Assert.Equal(ZipAdmissionState.Approved, (await f.Journal.ReadAdmissionAsync(p.OperationId))!.State);
    }

    [Theory]
    [InlineData("UPDATE ZipParentAdmissions SET RequestId='changed'")]
    [InlineData("DELETE FROM ZipParentAdmissions")]
    [InlineData("INSERT OR REPLACE INTO ZipParentAdmissions SELECT * FROM ZipParentAdmissions")]
    [InlineData("UPDATE ZipParentAdmissionEvents SET EvidenceJson='{}'")]
    [InlineData("DELETE FROM ZipParentAdmissionEvents")]
    [InlineData("INSERT OR REPLACE INTO ZipParentAdmissionEvents SELECT * FROM ZipParentAdmissionEvents")]
    public async Task Identity_and_authority_evidence_are_sql_immutable(string sql)
    {
        await using var f = await ZipAdmissionFixture.CreateAsync();
        var p = await f.Journal.ResolveOrReserveAdmissionAsync(f.Key, f.Binding);
        p = await f.Journal.AppendAdmissionEventAsync(p, f.Approval(p));
        await Assert.ThrowsAsync<SqliteException>(() => f.SqlAsync(sql));
        Assert.Equal(p, await f.Journal.ReadAdmissionAsync(p.OperationId));
    }

    [Fact]
    public async Task Cancellation_and_missing_execution_identity_create_no_partial_admission()
    {
        await using var f = await ZipAdmissionFixture.CreateAsync(); using var ct = new CancellationTokenSource(); ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Journal.ResolveOrReserveAdmissionAsync(f.Key, f.Binding, ct.Token));
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Journal.AcquireAdmissionExecutionAsync("missing-operation"));
        Assert.Equal(0, await f.ScalarAsync("SELECT COUNT(*) FROM ZipParentAdmissions"));
    }

    [Fact]
    public async Task Recovery_listing_is_bounded_and_paginates_original_operations_including_terminal_records()
    {
        await using var f = await ZipAdmissionFixture.CreateAsync();
        var operations = new List<string>();
        for (var i = 0; i < 3; i++) operations.Add((await f.Journal.ResolveOrReserveAdmissionAsync(f.Key with { RequestId = "request-" + i }, f.Binding)).OperationId);
        operations.Sort(StringComparer.Ordinal);
        var first = Assert.Single(await f.Journal.ReadRecoveryAdmissionsAsync(null, 1));
        Assert.Equal(operations[0], first.OperationId);
        Assert.Equal(operations.Skip(1), (await f.Journal.ReadRecoveryAdmissionsAsync(first.OperationId, 2)).Select(p => p.OperationId));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => f.Journal.ReadRecoveryAdmissionsAsync(null, 1025));
    }

    [Fact]
    public async Task Even_recorded_approval_does_not_activate_stock_or_legacy_publication()
    {
        await using var f = await ZipAdmissionFixture.CreateAsync();
        var p = await f.Journal.ResolveOrReserveAdmissionAsync(f.Key, f.Binding);
        await f.Journal.AppendAdmissionEventAsync(p, f.Approval(p));
        var repository = new SqliteEvidenceRepository(f.Path);
        await repository.AddArtifactAsync(new Artifact { Id = new("archive"), Name = "archive.zip", ArtifactType = "file",
            Metadata = new Dictionary<string, object> { [ArtifactMetadataKeys.FileExtension] = ".zip" } });
        var activity = InventoryConsoleCommand.CreateZipWorkflowActivity(repository);
        var result = await activity.ExecuteAsync(new() { WorkflowId = new("workflow") });
        Assert.False(result.Succeeded); Assert.Contains("Review is required", result.Message);
        Assert.Equal(0, await f.ScalarAsync("SELECT COUNT(*) FROM ZipExtractionParents"));
        Assert.Equal(0, await f.ScalarAsync("SELECT COUNT(*) FROM ZipExtractionRetentions"));
        Assert.Equal(0, await f.ScalarAsync("SELECT COUNT(*) FROM Relationships"));
        Assert.Equal(1, await f.ScalarAsync("SELECT COUNT(*) FROM Artifacts"));
    }
}
