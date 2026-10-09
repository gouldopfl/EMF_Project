using EMF.Discovery.Models;
using EMF.Inventory.Models;
using EMF.Integrity;
using EMF.Orchestration.Services;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class InventoryWorkflowActivityTests
{
    [Fact]
    public async Task Direct_content_store_cannot_bypass_authenticated_ingestion()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync();
        Assert.Throws<NotSupportedException>(() => new InventoryWorkflowActivity(f.Service,
            new EvidencePersistenceService(f.Ingestion.Repository), new Sha256ContentFingerprintService(), f.Ingestion.Physical, f.Source, new DiscoveryOptions()));
        Assert.Null(await f.Ingestion.Physical.ReadAsync(f.Ingestion.Id));
    }
    [Fact]
    public async Task ExecuteAsync_succeeds_when_all_inventory_results_succeed()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(); await f.SqlAsync(Path.Combine(f.Source, "b.sqlite"), "CREATE TABLE second(id INTEGER);");
        Assert.True((await f.Activity().ExecuteAsync(f.Context)).Succeeded);
        var s = (await f.Journal.LoadAsync(f.Context.OperationId!.Value.Value))!; Assert.Equal(1, s.ConfirmedOrdinal); Assert.Equal(InventoryParentStatus.Completed, s.Status);
        foreach (var item in s.Plan.Items) Assert.NotNull(await f.Ingestion.Repository.GetArtifactAsync(new(item.ArtifactId)));
    }
    [Fact]
    public async Task ExecuteAsync_preserves_earlier_metadata_when_later_inventory_fails()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(new() { MaximumTables = 1 });
        await f.SqlAsync(Path.Combine(f.Source, "b.sqlite"), "CREATE TABLE second(id INTEGER);CREATE TABLE excess(id INTEGER);");
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Activity().ExecuteAsync(f.Context));
        var s = (await f.Journal.LoadAsync(f.Context.OperationId!.Value.Value))!; Assert.Equal(0, s.ConfirmedOrdinal); Assert.Equal(InventoryParentStatus.RequiresReview, s.Status);
        Assert.NotNull(await f.Ingestion.Repository.GetArtifactAsync(new(s.Plan.Items[0].ArtifactId)));
        Assert.Null(await f.Ingestion.Repository.GetArtifactAsync(new(s.Plan.Items[1].ArtifactId)));
    }
    [Fact]
    public async Task ExecuteAsync_reuses_existing_metadata_without_content_publication()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync(); await f.Activity().ExecuteAsync(f.Context);
        var original = (await f.Journal.LoadAsync(f.Context.OperationId!.Value.Value))!;
        var retry = new EMF.Orchestration.Models.WorkflowExecutionContext { WorkflowId = f.Context.WorkflowId, OperationId = new(Guid.NewGuid().ToString("N")) };
        await f.Activity().ExecuteAsync(retry); var second = (await f.Journal.LoadAsync(retry.OperationId!.Value.Value))!;
        Assert.NotEqual(original.Plan.Items[0].ArtifactId, second.Plan.Items[0].ArtifactId);
        Assert.Null(await f.Ingestion.Repository.GetArtifactAsync(new(second.Plan.Items[0].ArtifactId)));
        Assert.Null(await f.Ingestion.Physical.ReadAsync(new(original.Plan.Items[0].ArtifactId)));
        Assert.Equal(0, second.ConfirmedOrdinal);
    }
}
