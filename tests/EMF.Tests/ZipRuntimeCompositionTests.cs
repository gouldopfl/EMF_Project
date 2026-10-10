using EMF.ConsoleApplication;
using EMF.Core.Contracts.Zip;
using EMF.Core.Models;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class ZipRuntimeCompositionTests
{
    private sealed class ForbiddenChildHost : IZipChildIngestionHost
    {
        public int Calls;
        public Task<ZipChildIngestionRuntime> OpenAsync(ZipParentSnapshot parent, ZipEntryProgress entry, CancellationToken ct = default)
        { Calls++; throw new InvalidOperationException("A child host cannot manufacture parent authority."); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stock_workflow_rejects_archive_without_reading_or_publishing_children(bool childHostPresent)
    {
        var repository = new InMemoryEvidenceRepository();
        var archive = new Artifact { Id = new("parent-zip"), Name = "archive.zip", ArtifactType = "file",
            Metadata = new Dictionary<string, object> { [ArtifactMetadataKeys.FileExtension] = ".zip" } };
        await repository.AddArtifactAsync(archive);
        var host = new ForbiddenChildHost();
        var runtime = childHostPresent ? new ZipRuntimeComposition(childIngestionHost: host) : null;
        var activity = InventoryConsoleCommand.CreateZipWorkflowActivity(repository, runtime);
        var result = await activity.ExecuteAsync(new() { WorkflowId = new("workflow-zip") });
        Assert.Equal("zip-archives", activity.Id);
        Assert.False(result.Succeeded);
        Assert.Contains("Review is required", result.Message);
        Assert.Equal(0, host.Calls);
        Assert.Single(await repository.GetArtifactsByMetadataAsync(ArtifactMetadataKeys.FileExtension, ".zip"));
        Assert.Empty(await repository.GetRelationshipsAsync(archive.Id));
    }

    [Fact]
    public async Task Non_zip_workload_succeeds_without_manufacturing_durable_host_authority()
    {
        var repository = new InMemoryEvidenceRepository();
        await repository.AddArtifactAsync(new Artifact { Id = new("text"), Name = "text.txt", ArtifactType = "file",
            Metadata = new Dictionary<string, object> { [ArtifactMetadataKeys.FileExtension] = ".txt" } });
        var result = await InventoryConsoleCommand.CreateZipWorkflowActivity(repository).ExecuteAsync(new() { WorkflowId = new("workflow") });
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Cancelled_default_zip_activity_does_no_work()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var activity = InventoryConsoleCommand.CreateZipWorkflowActivity(new InMemoryEvidenceRepository());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => activity.ExecuteAsync(new() { WorkflowId = new("workflow") }, cancelled.Token));
    }
}
