using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class ZipArchiveWorkflowActivityTests
{
    [Fact]
    public async Task ExecuteAsync_ProcessesPersistedArchive()
    {
        var repository =
            new InMemoryEvidenceRepository();

        var archive = new Artifact
        {
            Id = new ArtifactId("zip-001"),
            Name = "archive.zip",
            ArtifactType = "file",
            Metadata = new Dictionary<string, object>
            {
                [ArtifactMetadataKeys.FileExtension] = ".zip"
            }
        };

        await repository.AddArtifactAsync(archive);

        var store = new StubContentStore(
            archive.Id,
            "eml"u8.ToArray());

        var processor = new StubProcessingService();

        var activity =
            new ZipArchiveWorkflowActivity(
                repository,
                store,
                processor);

        var result =
            await activity.ExecuteAsync(
                new WorkflowExecutionContext
                {
                    WorkflowId =
                        new WorkflowId("workflow-zip")
                });

        Assert.True(result.Succeeded);
        Assert.Equal(1, processor.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_FailsWhenArchiveContentMissing()
    {
        var repository =
            new InMemoryEvidenceRepository();

        var archive = new Artifact
        {
            Id = new ArtifactId("zip-missing"),
            Name = "missing.zip",
            ArtifactType = "file",
            Metadata = new Dictionary<string, object>
            {
                [ArtifactMetadataKeys.FileExtension] = ".zip"
            }
        };

        await repository.AddArtifactAsync(archive);

        var processor = new StubProcessingService();

        var activity =
            new ZipArchiveWorkflowActivity(
                repository,
                new StubContentStore(),
                processor);

        var result =
            await activity.ExecuteAsync(
                new WorkflowExecutionContext
                {
                    WorkflowId =
                        new WorkflowId("workflow-zip")
                });

        Assert.False(result.Succeeded);
        Assert.Equal(0, processor.Calls);
    }

    private sealed class StubProcessingService :
        IZipArchiveProcessingService
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<ZipEntryExtractionResult>>
            ProcessAsync(
                ArtifactId archiveArtifactId,
                ReadOnlyMemory<byte> content,
                CancellationToken cancellationToken = default)
        {
            Calls++;

            return Task.FromResult<
                IReadOnlyList<ZipEntryExtractionResult>>(
                    Array.Empty<ZipEntryExtractionResult>());
        }
    }

    private sealed class StubContentStore :
        IArtifactContentStore
    {
        private readonly ArtifactId? _artifactId;
        private readonly byte[]? _content;

        public StubContentStore(
            ArtifactId? artifactId = null,
            byte[]? content = null)
        {
            _artifactId = artifactId;
            _content = content;
        }

        public Task<byte[]?> ReadAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                artifactId == _artifactId
                    ? _content
                    : null);

        public Task WriteAsync(
            ArtifactId artifactId,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
