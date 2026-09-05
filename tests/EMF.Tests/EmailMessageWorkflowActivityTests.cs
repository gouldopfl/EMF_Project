using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Core.Models.Integrity;
using EMF.Discovery.Services;
using EMF.Integrity;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;
using EMF.Discovery.Contracts;
using EMF.Discovery.Models;
using EMF.Orchestration.Services;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class EmailMessageWorkflowActivityTests
{
    [Fact]
    public async Task ExecuteAsync_PersistsDiscoveredEmail()
    {
        var sourcePath =
            Path.Combine(
                Path.GetTempPath(),
                $"emf-email-{Guid.NewGuid():N}");

        Directory.CreateDirectory(sourcePath);

        try
        {
            var emailPath =
                Path.Combine(sourcePath, "message.eml");

            await File.WriteAllBytesAsync(
                emailPath,
                "From: test@example.com\r\nSubject: Test\r\n\r\nHello"u8.ToArray());

            var repository =
                new InMemoryEvidenceRepository();

            var store =
                new RecordingContentStore();

            var activity =
                new EmailMessageWorkflowActivity(
                    new FileSystemDiscoveryService(),
                    repository,
                    store,
                    new Sha256ContentFingerprintService(),
                    new StubIdGenerator(),
                    new ArtifactFactory(),
                    sourcePath,
                    new DiscoveryOptions());

            var result =
                await activity.ExecuteAsync(
                    new WorkflowExecutionContext
                    {
                        WorkflowId =
                            new WorkflowId("workflow-email")
                    });

            Assert.True(result.Succeeded);

            var artifacts =
                await repository.GetArtifactsByMetadataAsync(
                    ArtifactMetadataKeys.FileExtension,
                    ".eml");

            Assert.Single(artifacts);
            Assert.Equal("message.eml", artifacts[0].Name);
            Assert.Single(store.Written);
        }
        finally
        {
            Directory.Delete(
                sourcePath,
                recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_IgnoresNonEmailFiles()
    {
        var sourcePath = Path.Combine(
            Path.GetTempPath(),
            $"emf-email-{Guid.NewGuid():N}");

        Directory.CreateDirectory(sourcePath);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(sourcePath, "notes.txt"),
                "not an email");

            var repository = new InMemoryEvidenceRepository();
            var store = new RecordingContentStore();

            var activity = CreateActivity(
                sourcePath, repository, store);

            var result =
                await activity.ExecuteAsync(CreateContext());

            Assert.True(result.Succeeded);
            Assert.Empty(store.Written);
        }
        finally
        {
            Directory.Delete(sourcePath, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ReusesExistingEmail()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-email-{Guid.NewGuid():N}");

        Directory.CreateDirectory(path);

        try
        {
            var file = Path.Combine(path, "message.eml");
            var content =
                "From: test@example.com\r\n\r\nHello"u8.ToArray();

            await File.WriteAllBytesAsync(file, content);

            var repository = new InMemoryEvidenceRepository();
            var fingerprint = new Sha256ContentFingerprintService();

            var item = new DiscoveredItem
            {
                Name = "message.eml",
                SourcePath = file,
                SourceType = "file"
            };

            var existing = new ArtifactFactory().Create(
                item,
                new ArtifactId("existing-email"),
                await fingerprint.ComputeAsync(content));

            await repository.AddArtifactWithProvenanceAsync(
                existing.Artifact,
                existing.Provenance);

            var store = new RecordingContentStore();

            var activity = CreateActivity(
                path, repository, store);

            var result =
                await activity.ExecuteAsync(CreateContext());

            Assert.True(result.Succeeded);
            Assert.Empty(store.Written);
        }
        finally
        {
            Directory.Delete(path, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_RejectsOversizedEmailWhenDiscoverySizeIsStale()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-email-{Guid.NewGuid():N}");

        Directory.CreateDirectory(path);

        try
        {
            await File.WriteAllBytesAsync(
                Path.Combine(path, "message.eml"),
                new byte[2]);

            var repository = new InMemoryEvidenceRepository();
            var store = new RecordingContentStore();

            var activity = new EmailMessageWorkflowActivity(
                new SingleItemDiscoveryService(
                    new DiscoveredItem
                    {
                        Name = "message.eml",
                        SourcePath = Path.Combine(path, "message.eml"),
                        SourceType = "file",
                        SizeBytes = 1
                    }),
                repository,
                store,
                new Sha256ContentFingerprintService(),
                new StubIdGenerator(),
                new ArtifactFactory(),
                path,
                new DiscoveryOptions(),
                maxMessageBytes: 1);

            var result =
                await activity.ExecuteAsync(CreateContext());

            Assert.False(result.Succeeded);
            Assert.Empty(store.Written);
        }
        finally
        {
            Directory.Delete(path, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_CleansUpWhenPersistenceFails()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-email-{Guid.NewGuid():N}");

        Directory.CreateDirectory(path);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(path, "message.eml"),
                "From: test@example.com\r\n\r\nHello");

            var store = new RecordingContentStore();

            var activity = new EmailMessageWorkflowActivity(
                new FileSystemDiscoveryService(),
                new FailingRepository(),
                store,
                new Sha256ContentFingerprintService(),
                new StubIdGenerator(),
                new ArtifactFactory(),
                path,
                new DiscoveryOptions());

            var result =
                await activity.ExecuteAsync(CreateContext());

            Assert.False(result.Succeeded);
            Assert.Single(store.Written);
            Assert.Single(store.Deleted);
        }
        finally
        {
            Directory.Delete(path, true);
        }
    }

    private static EmailMessageWorkflowActivity CreateActivity(
        string path,
        InMemoryEvidenceRepository repository,
        RecordingContentStore store) =>
        new(
            new FileSystemDiscoveryService(),
            repository,
            store,
            new Sha256ContentFingerprintService(),
            new StubIdGenerator(),
            new ArtifactFactory(),
            path,
            new DiscoveryOptions());

    private static WorkflowExecutionContext CreateContext() =>
        new()
        {
            WorkflowId = new WorkflowId("workflow-email")
        };

    private sealed class SingleItemDiscoveryService :
        IStreamingDiscoveryService
    {
        private readonly DiscoveredItem _item;

        public SingleItemDiscoveryService(
            DiscoveredItem item)
        {
            _item = item;
        }

        public async IAsyncEnumerable<DiscoveredItem>
            DiscoverItemsAsync(
                string sourcePath,
                DiscoveryOptions options,
                [System.Runtime.CompilerServices.EnumeratorCancellation]
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield return _item;
        }
    }

    private sealed class StubIdGenerator :
        IArtifactIdGenerator
    {
        private int _counter;

        public ArtifactId Generate() =>
            new($"email-{++_counter}");
    }

    private sealed class RecordingContentStore :
        IArtifactContentStore
    {
        public List<ArtifactId> Written { get; } = [];
        public List<ArtifactId> Deleted { get; } = [];

        public Task WriteAsync(
            ArtifactId artifactId,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default)
        {
            Written.Add(artifactId);
            return Task.CompletedTask;
        }

        public Task<byte[]?> ReadAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<byte[]?>(null);

        public Task DeleteAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default)
        {
            Deleted.Add(artifactId);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingRepository : IEvidenceRepository
    {
        private readonly InMemoryEvidenceRepository _inner = new();

        public Task<Artifact?> FindArtifactAsync(
            string s, ContentFingerprint f,
            CancellationToken c = default) =>
            _inner.FindArtifactAsync(s, f, c);

        public Task AddArtifactWithProvenanceAsync(
            Artifact a, Provenance p,
            CancellationToken c = default) =>
            throw new InvalidOperationException("Persistence failed.");

        public Task AddArtifactAsync(Artifact a, CancellationToken c = default) =>
            _inner.AddArtifactAsync(a, c);

        public Task AddRelationshipAsync(Relationship r, CancellationToken c = default) =>
            _inner.AddRelationshipAsync(r, c);

        public Task<Artifact?> GetArtifactAsync(ArtifactId id, CancellationToken c = default) =>
            _inner.GetArtifactAsync(id, c);

        public Task<EvidenceAggregate?> GetEvidenceAggregateAsync(
            ArtifactId id, CancellationToken c = default) =>
            _inner.GetEvidenceAggregateAsync(id, c);

        public Task MergeArtifactMetadataAsync(
            ArtifactId id,
            IReadOnlyDictionary<string, object> metadata,
            CancellationToken c = default) =>
            _inner.MergeArtifactMetadataAsync(id, metadata, c);

        public Task<IReadOnlyList<Artifact>> GetArtifactsByMetadataAsync(
            string k, string v, CancellationToken c = default) =>
            _inner.GetArtifactsByMetadataAsync(k, v, c);

        public Task<IReadOnlyList<Relationship>> GetRelationshipsAsync(
            ArtifactId id, CancellationToken c = default) =>
            _inner.GetRelationshipsAsync(id, c);

        public Task AddProvenanceAsync(Provenance p, CancellationToken c = default) =>
            _inner.AddProvenanceAsync(p, c);

        public Task AddArtifactWithProvenanceAndRelationshipsAsync(
            Artifact a, Provenance p,
            IReadOnlyCollection<Relationship> r,
            CancellationToken c = default) =>
            _inner.AddArtifactWithProvenanceAndRelationshipsAsync(a, p, r, c);

        public Task<IReadOnlyList<Provenance>> GetProvenanceAsync(
            ArtifactId id, CancellationToken c = default) =>
            _inner.GetProvenanceAsync(id, c);
    }

}
