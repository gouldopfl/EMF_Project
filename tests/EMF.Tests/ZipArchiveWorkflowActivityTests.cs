using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Contracts;
using EMF.Integrity;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class ZipArchiveWorkflowActivityTests
{
    [Fact]
    public async Task UnsupportedStoreRejectedWithoutOrdinaryReadFallback()
    {
        var repository = new InMemoryEvidenceRepository();
        var activity = new ZipArchiveWorkflowActivity(repository, new UnboundedStore(), new StubProcessingService(),
            new ContainerProcessingGuard(repository, new Sha256ContentFingerprintService()));
        await Assert.ThrowsAsync<NotSupportedException>(() => activity.ExecuteAsync(new() { WorkflowId = new("zip") }));
    }
    [Fact]
    public async Task ProviderTooSmallRejectedBeforeParentRead()
    {
        var repository = new InMemoryEvidenceRepository();
        var store = new StubContentStore { MaximumStoredRepresentationBytes = 44767424 };
        var activity = new ZipArchiveWorkflowActivity(repository, store, new StubProcessingService(),
            new ContainerProcessingGuard(repository, new Sha256ContentFingerprintService()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => activity.ExecuteAsync(new() { WorkflowId = new("zip") }));
        Assert.Equal(0, store.BoundedReads);
    }
    private sealed class UnboundedStore : IArtifactContentStore
    {
        public Task<byte[]?> ReadAsync(ArtifactId id, CancellationToken ct = default) => throw new Exception("Ordinary fallback forbidden");
        public Task WriteAsync(ArtifactId id, ReadOnlyMemory<byte> content, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(ArtifactId id, CancellationToken ct = default) => throw new NotSupportedException();
    }

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
                processor,
                new ContainerProcessingGuard(
                    repository,
                    new Sha256ContentFingerprintService()));

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
    public async Task ExecuteAsync_SkipsAlreadyProcessedUnchangedArchive()
    {
        var repository =
            new InMemoryEvidenceRepository();

        var archive = new Artifact
        {
            Id = new ArtifactId("zip-repeat"),
            Name = "repeat.zip",
            ArtifactType = "file",
            Metadata = new Dictionary<string, object>
            {
                [ArtifactMetadataKeys.FileExtension] = ".zip"
            }
        };

        await repository.AddArtifactAsync(archive);

        var store =
            new StubContentStore(
                archive.Id,
                "zip"u8.ToArray());

        var processor = new StubProcessingService();

        var activity =
            new ZipArchiveWorkflowActivity(
                repository,
                store,
                processor,
                new ContainerProcessingGuard(
                    repository,
                    new Sha256ContentFingerprintService()));

        await activity.ExecuteAsync(
            new WorkflowExecutionContext
            {
                WorkflowId = new WorkflowId("workflow-zip-first")
            });

        await activity.ExecuteAsync(
            new WorkflowExecutionContext
            {
                WorkflowId = new WorkflowId("workflow-zip-second")
            });

        Assert.Equal(1, processor.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsExcessiveContainerDepth()
    {
        var repository =
            new InMemoryEvidenceRepository();

        var parent = new Artifact
        {
            Id = new ArtifactId("zip-parent"),
            Name = "parent.eml",
            ArtifactType = "file",
            Metadata = new Dictionary<string, object>
            {
                [ArtifactMetadataKeys.FileExtension] = ".eml"
            }
        };

        var archive = new Artifact
        {
            Id = new ArtifactId("zip-child"),
            Name = "child.zip",
            ArtifactType = "file",
            Metadata = new Dictionary<string, object>
            {
                [ArtifactMetadataKeys.FileExtension] = ".zip"
            }
        };

        await repository.AddArtifactAsync(parent);
        await repository.AddArtifactAsync(archive);

        await repository.AddRelationshipAsync(
            new Relationship
            {
                SourceArtifactId = archive.Id,
                TargetArtifactId = parent.Id,
                RelationshipType = RelationshipTypes.DerivedFrom
            });

        var processor = new StubProcessingService();

        var activity =
            new ZipArchiveWorkflowActivity(
                repository,
                new StubContentStore(
                    archive.Id,
                    "zip"u8.ToArray()),
                processor,
                new ContainerProcessingGuard(
                    repository,
                    new Sha256ContentFingerprintService()),
                new ContainerAncestryGuard(
                    repository,
                    maxContainerDepth: 1));

        var result =
            await activity.ExecuteAsync(
                new WorkflowExecutionContext
                {
                    WorkflowId =
                        new WorkflowId("workflow-zip-depth")
                });

        Assert.False(result.Succeeded);
        Assert.Equal(0, processor.Calls);
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
                processor,
                new ContainerProcessingGuard(
                    repository,
                    new Sha256ContentFingerprintService()));

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

        public Task<IReadOnlyList<ZipEntryExtractionResult>> ProcessAsync(ArtifactId id, Stream content,
            CancellationToken cancellationToken = default)
        {
            Assert.True(content.CanSeek);
            Calls++;
            return Task.FromResult<IReadOnlyList<ZipEntryExtractionResult>>(Array.Empty<ZipEntryExtractionResult>());
        }

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
        IArtifactContentStore, IBoundedVersionedArtifactContentStore
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

        public long MaximumStoredRepresentationBytes { get; set; } = 150L * 1024 * 1024;
        public int BoundedReads;
        public Task<IArtifactContentReadLease?> ReadBoundedVersionedAsync(ArtifactId id,
            BoundedArtifactContentReadRequest request, CancellationToken cancellationToken = default) =>
            ReadLease(id);
        private Task<IArtifactContentReadLease?> ReadLease(ArtifactId id)
        {
            BoundedReads++;
            return Task.FromResult<IArtifactContentReadLease?>(id == _artifactId && _content is not null
                ? new ArtifactContentReadLease(id, new("test-revision"), _content.Length, (byte[])_content.Clone()) : null);
        }

        public Task<byte[]?> ReadAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            throw new Exception("Ordinary ZIP read fallback forbidden");

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
