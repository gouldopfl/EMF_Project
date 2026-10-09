using System.Text.Json;
using EMF.Core.Contracts.Ingestion;
using EMF.Inventory.Models;
using EMF.Tests.TestInfrastructure;
using EMF.ConsoleApplication;
using EMF.Orchestration.Services;
namespace EMF.Tests;

public sealed class InventoryMigrationWorkflowTests
{
    [Theory]
    [InlineData("BeforeChildStart")]
    [InlineData("AfterChildEffect")]
    [InlineData("AfterParentConfirmation")]
    public async Task Metadata_restart_preserves_plan_and_confirms_exactly_once(string interruption)
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(); var stopped = false;
        await Assert.ThrowsAsync<Interrupted>(() => f.Activity(checkpoint: (stage, ct) => { if (stage == interruption && !stopped) { stopped = true; throw new Interrupted(); } return Task.CompletedTask; }).ExecuteAsync(f.Context));
        var old = await f.Journal.LoadAsync(f.Context.OperationId!.Value.Value); Assert.NotNull(old); var child = old.Plan.Items[0].ChildOperationId;
        File.Delete(f.SourceFile); await f.RestartAsync(); Assert.True((await f.Activity().ExecuteAsync(f.Context)).Succeeded);
        var state = await f.Journal.LoadAsync(old.Plan.ParentId); Assert.Equal(child, state!.Plan.Items[0].ChildOperationId); Assert.Equal(0, state.ConfirmedOrdinal); Assert.Equal(InventoryParentStatus.Completed, state.Status);
        Assert.Null(await f.Ingestion.Physical.ReadAsync(new(state.Plan.Items[0].ArtifactId))); Assert.Equal(InventoryRetentionStatus.Released, (await f.Journal.ReadRetentionAsync(state.Plan.Items[0].Retained.ObjectId))!.Status);
    }
    [Theory]
    [InlineData("BeforeChildStart")]
    [InlineData("AfterChildEffect")]
    [InlineData("AfterParentConfirmation")]
    public async Task Protected_restart_reconciles_same_child_without_repeat_publication(string interruption)
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(); var stopped = false;
        await Assert.ThrowsAsync<Interrupted>(() => f.Activity(InventoryMode.ProtectedContent, (stage, ct) => { if (stage == interruption && !stopped) { stopped = true; throw new Interrupted(); } return Task.CompletedTask; }).ExecuteAsync(f.Context));
        var old = (await f.Journal.LoadAsync(f.Context.OperationId!.Value.Value))!; var item = old.Plan.Items[0];
        Assert.Equal(InventoryParentStatus.Active, old.Status);
        if (interruption == "AfterChildEffect")
        {
            Assert.Equal(-1, old.ConfirmedOrdinal);
            Assert.Equal(InventoryRetentionStatus.Sealed, (await f.Journal.ReadRetentionAsync(item.Retained.ObjectId))!.Status);
        }
        var adoptedBeforeRestart = await f.Ingestion.Physical.ReadVersionedAsync(new(item.ArtifactId));
        await using (var proof = await f.Ingestion.Persistence.AcquireAsync(new(item.ChildOperationId)))
        {
            if (interruption == "BeforeChildStart") Assert.Null(proof.Intent);
            else Assert.True(await proof.HasAdoptionEvidenceAsync(new(item.ArtifactId)));
        }
        Assert.Equal(interruption == "AfterParentConfirmation" ? 0 : -1, old.ConfirmedOrdinal);
        File.Delete(f.SourceFile); await f.RestartAsync();
        Assert.True((await f.Activity(InventoryMode.ProtectedContent).ExecuteAsync(f.Context)).Succeeded);
        var state = (await f.Journal.LoadAsync(old.Plan.ParentId))!; Assert.Equal(item, state.Plan.Items[0]); Assert.Equal(0, state.ConfirmedOrdinal); Assert.Equal(InventoryParentStatus.Completed, state.Status);
        Assert.Equal(interruption == "BeforeChildStart" ? 1 : 0, f.Ingestion.Encryption.Encryptions);
        var adoptedAfterRestart = await f.Ingestion.Physical.ReadVersionedAsync(new(item.ArtifactId));
        Assert.NotNull(adoptedAfterRestart);
        if (adoptedBeforeRestart is not null) Assert.Equal(adoptedBeforeRestart.Revision, adoptedAfterRestart.Revision);
        Assert.Equal(InventoryRetentionStatus.Released, (await f.Journal.ReadRetentionAsync(item.Retained.ObjectId))!.Status);
    }
    [Theory]
    [InlineData("Prepared")]
    [InlineData("ContentCreated")]
    public async Task Prepared_child_reuses_original_identity_and_retained_bytes(string checkpoint)
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(); var s = await f.PlanAsync(InventoryMode.ProtectedContent); var item = s.Plan.Items[0];
        // Inject the durable commit interruption into the authenticated workflow composition,
        // not by calling the coordinator directly. First-child handoff must inspect and then
        // materialize the admitted retained snapshot before the Prepared commit is reached.
        var decryptions = 0;
        int? decryptionsBeforeCandidateEncryption = null;
        f.Ingestion.Encryption.DecryptHook = () => { decryptions++; return Task.CompletedTask; };
        f.Ingestion.Encryption.EncryptHook = () => { decryptionsBeforeCandidateEncryption = decryptions; return Task.CompletedTask; };
        var fault = new ArtifactIngestionFixture.FaultPersistence(f.Ingestion.Persistence, checkpoint);
        f.ChildPersistenceOverride = _ => fault;
        await Assert.ThrowsAsync<ArtifactIngestionFixture.SimulatedCrashException>(() => f.Activity(InventoryMode.ProtectedContent).ExecuteAsync(f.Context));
        // The coordinator also decrypts its staged candidate before ContentCreated.
        // Verify inspection plus content materialization before that candidate work.
        if (checkpoint == "Prepared")
        {
            Assert.Equal(2, decryptions);
            Assert.Null(decryptionsBeforeCandidateEncryption);
        }
        else Assert.Equal(2, decryptionsBeforeCandidateEncryption);
        await using (var prepared = await f.Ingestion.Persistence.AcquireAsync(new(item.ChildOperationId)))
        {
            Assert.Equal(checkpoint == "Prepared" ? ArtifactIngestionState.Prepared : ArtifactIngestionState.ContentCreated, prepared.Intent!.State);
            Assert.Equal(item.ArtifactId, prepared.Intent.ArtifactId.Value);
            Assert.Equal(item.ChildOperationId, prepared.OperationBinding!.OperationId.Value);
            Assert.Equal(s.Plan.ParentId, prepared.OperationBinding.ParentOperationId!.Value.Value);
            Assert.False(await prepared.HasAdoptionEvidenceAsync(new(item.ArtifactId)));
        }
        Assert.Null(await f.Ingestion.Repository.GetArtifactAsync(new(item.ArtifactId)));
        Assert.Equal(-1, (await f.Journal.LoadAsync(s.Plan.ParentId))!.ConfirmedOrdinal);
        File.Delete(f.SourceFile);
        await f.RestartAsync();
        var result = await f.Activity(InventoryMode.ProtectedContent).ExecuteAsync(f.Context);
        Assert.True(result.Succeeded);
        var final = (await f.Journal.LoadAsync(s.Plan.ParentId))!;
        Assert.Equal(item, final.Plan.Items[0]); Assert.Equal(0, final.ConfirmedOrdinal);
        Assert.Equal(InventoryParentStatus.Completed, final.Status);
        Assert.Equal(checkpoint == "Prepared" ? 1 : 0, f.Ingestion.Encryption.Encryptions);
        await using var adopted = await f.Ingestion.Persistence.AcquireAsync(new(item.ChildOperationId));
        Assert.Equal(ArtifactIngestionState.Completed, adopted.Intent!.State);
        Assert.True(await adopted.HasAdoptionEvidenceAsync(new(item.ArtifactId)));
        Assert.Equal(InventoryRetentionStatus.Released, (await f.Journal.ReadRetentionAsync(item.Retained.ObjectId))!.Status);
    }
    [Fact]
    public async Task Canonical_dedup_is_confirmed_even_when_provisional_is_not_adopted()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(); var s = await f.PlanAsync(InventoryMode.ProtectedContent); var item = s.Plan.Items[0];
        var provisional = JsonSerializer.Deserialize<IngestionMetadataDraft>(item.DraftJson)!;
        var canonical = new IngestionMetadataDraft(new() { Id = new("canonical-inventory"), Name = provisional.Artifact.Name, ArtifactType = provisional.Artifact.ArtifactType, Fingerprint = provisional.Artifact.Fingerprint },
            new() { ArtifactId = new("canonical-inventory"), Source = item.SourceLocator, RecordedBy = provisional.Provenance.RecordedBy });
        var bytes = await f.Service.ReadContentAsync(item, default); await f.Ingestion.Service().IngestAsync(canonical, bytes); Array.Clear(bytes);
        await Assert.ThrowsAsync<Interrupted>(() => f.Activity(InventoryMode.ProtectedContent, (stage, ct) => stage == "AfterChildEffect" ? Task.FromException(new Interrupted()) : Task.CompletedTask).ExecuteAsync(f.Context));
        File.Delete(f.SourceFile); await f.RestartAsync(); await f.Activity(InventoryMode.ProtectedContent).ExecuteAsync(f.Context); Assert.Equal(0, f.Ingestion.Encryption.Encryptions);
        await using var session = await f.Ingestion.Persistence.AcquireAsync(new(item.ChildOperationId));
        Assert.Equal(ArtifactIngestionDisposition.DeduplicatedToCanonicalArtifact, session.Intent!.Disposition);
        Assert.Equal("canonical-inventory", session.Intent.CanonicalArtifactId!.Value.Value);
        Assert.Equal(0, (await f.Journal.LoadAsync(s.Plan.ParentId))!.ConfirmedOrdinal);
    }
    [Fact]
    public async Task Restart_recreates_security_and_encryption_from_host_configuration_not_old_provider_state()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync();
        var admitted = await f.PlanAsync(InventoryMode.ProtectedContent);
        f.Ingestion.Classification.Classification = null;
        f.Ingestion.Authorization.AllowIngestion = false;
        f.Ingestion.Authorization.AllowRecovery = false;
        f.Ingestion.SecurityContext.AllowReview = false;
        f.Ingestion.Encryption.DecryptHook = () => throw new InvalidOperationException("Dying provider must not be reused.");
        File.Delete(f.SourceFile);
        await f.RestartAsync();
        Assert.Equal("Confidential", f.Ingestion.Classification.Classification);
        Assert.True(f.Ingestion.Authorization.AllowIngestion);
        Assert.True(f.Ingestion.Authorization.AllowRecovery);
        Assert.True(f.Ingestion.SecurityContext.AllowReview);
        Assert.Null(f.Ingestion.Encryption.DecryptHook);
        Assert.True((await f.Activity(InventoryMode.ProtectedContent).ExecuteAsync(f.Context)).Succeeded);
        var completed = (await f.Journal.LoadAsync(admitted.Plan.ParentId))!;
        Assert.Equal(admitted.Plan.Items[0], completed.Plan.Items[0]);
        Assert.Equal(InventoryParentStatus.Completed, completed.Status);
    }
    [Fact]
    public async Task Missing_classification_preserves_retained_input_and_never_confirms()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(); f.Ingestion.Classification.Classification = null;
        await Assert.ThrowsAnyAsync<Exception>(() => f.Activity(InventoryMode.ProtectedContent).ExecuteAsync(f.Context));
        var s = (await f.Journal.LoadAsync(f.Context.OperationId!.Value.Value))!; Assert.Equal(-1, s.ConfirmedOrdinal); Assert.Equal(InventoryRetentionStatus.Sealed, (await f.Journal.ReadRetentionAsync(s.Plan.Items[0].Retained.ObjectId))!.Status);
    }
    [Fact]
    public async Task Stale_authority_revision_rejects_restart_before_child_continuation()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(); var s = await f.PlanAsync(InventoryMode.ProtectedContent); f.Authority = f.Authority with { Revision = "revision-2" };
        await f.RestartAsync();
        var e = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Activity(InventoryMode.ProtectedContent).ExecuteAsync(f.Context)); Assert.Contains("Stale authority", e.Message); Assert.Equal(-1, (await f.Journal.LoadAsync(s.Plan.ParentId))!.ConfirmedOrdinal);
    }
    [Fact]
    public async Task Authority_change_during_encryption_prevents_irreversible_adoption()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(); var s = await f.PlanAsync(InventoryMode.ProtectedContent); f.Ingestion.Encryption.EncryptHook = () => { f.Authority = f.Authority with { Revision = "revision-2" }; return Task.CompletedTask; };
        await Assert.ThrowsAnyAsync<Exception>(() => f.Activity(InventoryMode.ProtectedContent).ExecuteAsync(f.Context));
        Assert.Null(await f.Ingestion.Repository.GetArtifactAsync(new(s.Plan.Items[0].ArtifactId))); Assert.Equal(-1, (await f.Journal.LoadAsync(s.Plan.ParentId))!.ConfirmedOrdinal);
    }
    [Theory]
    [InlineData(InventoryMode.MetadataOnly)]
    [InlineData(InventoryMode.ProtectedContent)]
    public async Task Earlier_confirmation_survives_later_failure_and_restart_begins_at_next_ordinal(InventoryMode mode)
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(); await f.SqlAsync(Path.Combine(f.Source, "b.sqlite"), "CREATE TABLE second(id INTEGER);"); var starts = 0;
        await Assert.ThrowsAsync<Interrupted>(() => f.Activity(mode, checkpoint: (stage, ct) => { if (stage == "BeforeChildStart" && ++starts == 2) throw new Interrupted(); return Task.CompletedTask; }).ExecuteAsync(f.Context));
        var old = (await f.Journal.LoadAsync(f.Context.OperationId!.Value.Value))!; Assert.Equal(0, old.ConfirmedOrdinal);
        foreach (var source in Directory.EnumerateFiles(f.Source)) File.Delete(source);
        await f.RestartAsync(); starts = 0; await f.Activity(mode, checkpoint: (stage, ct) => { if (stage == "BeforeChildStart") starts++; return Task.CompletedTask; }).ExecuteAsync(f.Context);
        Assert.Equal(1, starts);
        var completed = (await f.Journal.LoadAsync(old.Plan.ParentId))!;
        Assert.Equal(old.Plan.Items, completed.Plan.Items); Assert.Equal(1, completed.ConfirmedOrdinal);
        Assert.Equal(InventoryParentStatus.Completed, completed.Status);
        Assert.Equal(mode == InventoryMode.ProtectedContent ? 1 : 0, f.Ingestion.Encryption.Encryptions);
        foreach (var item in completed.Plan.Items)
            Assert.Equal(InventoryRetentionStatus.Released, (await f.Journal.ReadRetentionAsync(item.Retained.ObjectId))!.Status);
    }
    [Fact]
    public async Task Stock_console_and_missing_protected_capability_fail_before_admission()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync();
        Assert.Throws<UnauthorizedAccessException>(() => new InventoryWorkflowActivity(f.Service, new EvidencePersistenceService(f.Ingestion.Repository), f.Source, new(), InventoryMode.ProtectedContent));
        Assert.Equal(2, await InventoryConsoleCommand.RunAsync(new[] { f.Source }, null)); Assert.Null(await f.Journal.LoadAsync(f.Context.OperationId!.Value.Value));
    }
    [Fact]
    public async Task Wrong_authenticated_child_binding_is_rejected_before_any_ingestion()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(); var s = await f.PlanAsync(InventoryMode.ProtectedContent); var item = s.Plan.Items[0];
        var adapter = new InventoryChildIngestionAdapter((expected, ct) => Task.FromResult(f.Authority), (parent, child, ct) => Task.FromResult(new InventoryAuthenticatedChild(
            f.Ingestion.Persistence, f.Ingestion.Physical, f.Ingestion.Staging, f.Ingestion.Encryption, f.Ingestion.SecurityContext, f.Ingestion.Classification, f.Ingestion.Authorization, f.Ingestion.Audit, f.Ingestion.Fingerprints)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => adapter.ExecuteAsync(s.Plan, item, ct => f.Service.ReadContentAsync(item, ct), default));
        await using var session = await f.Ingestion.Persistence.AcquireAsync(new(item.ChildOperationId)); Assert.Null(session.Intent);
    }
    [Fact]
    public async Task Cancellation_preserves_admitted_retention_for_recovery()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(); using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Activity(checkpoint: (stage, ct) => { if (stage == "BeforeChildStart") { cancel.Cancel(); ct.ThrowIfCancellationRequested(); } return Task.CompletedTask; }).ExecuteAsync(f.Context, cancel.Token));
        var s = (await f.Journal.LoadAsync(f.Context.OperationId!.Value.Value))!; Assert.Equal(-1, s.ConfirmedOrdinal); Assert.Equal(InventoryRetentionStatus.Sealed, (await f.Journal.ReadRetentionAsync(s.Plan.Items[0].Retained.ObjectId))!.Status);
        await f.RestartAsync(); await f.Activity().ExecuteAsync(f.Context); Assert.Equal(0, (await f.Journal.LoadAsync(s.Plan.ParentId))!.ConfirmedOrdinal);
    }
    [Fact]
    public async Task Detached_child_encryption_does_not_hold_parent_or_metadata_transaction()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(); var s = await f.PlanAsync(InventoryMode.ProtectedContent);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Ingestion.Encryption.EncryptHook = async () => { entered.TrySetResult(); await release.Task.ConfigureAwait(false); };
        var work = f.Activity(InventoryMode.ProtectedContent).ExecuteAsync(f.Context); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await f.Journal.ReserveRetentionAsync(new("other-parent", "other-retained", "owner", "create", "delete", InventoryRetentionStatus.Reserved)).WaitAsync(TimeSpan.FromSeconds(5));
            await using var unrelated = await f.Ingestion.Persistence.AcquireAsync(new("other-child")).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { release.TrySetResult(); }
        await work;
    }
    [Theory]
    [InlineData(InventoryMode.MetadataOnly)]
    [InlineData(InventoryMode.ProtectedContent)]
    public async Task Provider_bounds_apply_to_both_modes_before_child_effect(InventoryMode mode)
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(new() { MaximumTables = 1 }); await f.SqlAsync(f.SourceFile, "CREATE TABLE second(id INTEGER);");
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Activity(mode).ExecuteAsync(f.Context));
        var s = (await f.Journal.LoadAsync(f.Context.OperationId!.Value.Value))!; Assert.Equal(-1, s.ConfirmedOrdinal); Assert.Equal(InventoryParentStatus.RequiresReview, s.Status);
        Assert.Null(await f.Ingestion.Repository.GetArtifactAsync(new(s.Plan.Items[0].ArtifactId)));
    }
    [Fact]
    public async Task Lost_started_candidate_is_terminal_and_never_regenerated()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(); await f.SqlAsync(Path.Combine(f.Source, "b.sqlite"), "CREATE TABLE second(id INTEGER);"); var s = await f.PlanAsync(InventoryMode.ProtectedContent); var item = s.Plan.Items[0];
        var fault = new ArtifactIngestionFixture.FaultPersistence(f.Ingestion.Persistence, "PreparationStarted");
        f.ChildPersistenceOverride = _ => fault;
        await Assert.ThrowsAsync<ArtifactIngestionFixture.SimulatedCrashException>(() => f.Activity(InventoryMode.ProtectedContent).ExecuteAsync(f.Context));
        File.Delete(f.SourceFile); await f.RestartAsync();
        await Assert.ThrowsAsync<EMF.Orchestration.Contracts.InventoryChildRejectedException>(() => f.Activity(InventoryMode.ProtectedContent).ExecuteAsync(f.Context));
        var rejected = (await f.Journal.LoadAsync(s.Plan.ParentId))!; Assert.Equal(InventoryParentStatus.RequiresReview, rejected.Status); Assert.Equal(-1, rejected.ConfirmedOrdinal);
        Assert.Equal(0, f.Ingestion.Encryption.Encryptions); Assert.Equal(InventoryRetentionStatus.Sealed, (await f.Journal.ReadRetentionAsync(item.Retained.ObjectId))!.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Activity(InventoryMode.ProtectedContent).ExecuteAsync(f.Context));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Journal.StartNextAsync(rejected, 1));
        await using var next = await f.Ingestion.Persistence.AcquireAsync(new(s.Plan.Items[1].ChildOperationId)); Assert.Null(next.Intent);
    }
    [Fact]
    public async Task Reconciliation_skips_content_work_only_for_exact_durable_child_binding()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(); var s = await f.PlanAsync(InventoryMode.ProtectedContent); var item = s.Plan.Items[0];
        await f.Child().ExecuteAsync(s.Plan, item, ct => f.Service.ReadContentAsync(item, ct), default);
        await f.RestartAsync(); var reads = 0;
        var confirmation = await f.Child().ExecuteAsync(s.Plan, item, ct => { reads++; throw new InvalidOperationException("Reconciliation must not inspect/materialize again."); }, default);
        Assert.Equal(0, reads); Assert.Equal(InventoryConfirmationDisposition.ContentAdopted, confirmation.Disposition);
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Child().ExecuteAsync(s.Plan, item with { ChildOperationId = "replacement-child" }, ct => f.Service.ReadContentAsync(item, ct), default));
    }
    private sealed class Interrupted : Exception;
}
