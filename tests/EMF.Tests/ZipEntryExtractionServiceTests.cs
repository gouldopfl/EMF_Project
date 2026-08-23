using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Core.Models.Integrity;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class ZipEntryExtractionServiceTests
{
    [Fact]
    public async Task ExtractAsync_PersistsAttachment()
    {
        var repository = new RecordingRepository();
        var store = new RecordingContentStore();
        var service = CreateService(repository, store);

        var result = await service.ExtractAsync(
            new ArtifactId("archive-001"),
            "medical-record.pdf",
            "attachment bytes"u8.ToArray());

        Assert.Equal("medical-record.pdf", result.Artifact.Name);
        Assert.Equal("zip-entry", result.Artifact.ArtifactType);
        Assert.Equal(2, result.Relationships.Count);
        Assert.Single(repository.Persisted);
        Assert.Single(store.Written);
    }

    [Fact]
    public async Task ExtractAsync_DeletesContentWhenPersistenceFails()
    {
        var repository = new RecordingRepository
        {
            FailPersistence = true
        };

        var store = new RecordingContentStore();
        var service = CreateService(repository, store);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ExtractAsync(
                new ArtifactId("archive-002"),
                "record.txt",
                "content"u8.ToArray()));

        Assert.Single(store.Deleted);
    }

    [Fact]
    public async Task ExtractAsync_AggregatesCleanupFailure()
    {
        var repository = new RecordingRepository
        {
            FailPersistence = true
        };

        var store = new RecordingContentStore
        {
            FailDelete = true
        };

        var service = CreateService(repository, store);

        var exception = await Assert.ThrowsAsync<AggregateException>(
            () => service.ExtractAsync(
                new ArtifactId("archive-003"),
                "text/plain",
                "content"u8.ToArray()));

        Assert.Equal(2, exception.InnerExceptions.Count);
    }

    [Fact]
    public async Task ExtractAsync_ReusesExistingAttachment()
    {
        var repository =
            new TestInfrastructure.InMemoryEvidenceRepository();

        var existing = new Artifact
        {
            Id = new ArtifactId("existing-attachment"),
            Name = "record.txt",
            ArtifactType = "zip-entry",
            Fingerprint = new ContentFingerprint
            {
                Algorithm = "SHA256",
                Value = "test-fingerprint"
            }
        };

        await repository.AddArtifactWithProvenanceAsync(
            existing,
            new Provenance
            {
                ArtifactId = existing.Id,
                Source = "archive-005/record.txt",
                RecordedBy = "EMF.Discovery"
            });

        var store = new RecordingContentStore();
        var service = CreateService(repository, store);

        var result = await service.ExtractAsync(
            new ArtifactId("archive-005"),
            "record.txt",
            "content"u8.ToArray());

        Assert.Equal(existing.Id, result.Artifact.Id);
        Assert.Empty(store.Written);
    }

    private static ZipEntryExtractionService CreateService(
        IEvidenceRepository repository,
        IArtifactContentStore store) =>
        new(
            repository,
            store,
            new StubFingerprintService(),
            new StubIdGenerator(),
            new ArtifactFactory());

    private sealed class StubIdGenerator : IArtifactIdGenerator
    {
        public ArtifactId Generate() =>
            new("attachment-001");
    }

    private sealed class StubFingerprintService :
        IContentFingerprintService
    {
        public Task<ContentFingerprint> ComputeAsync(
            string sourcePath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ContentFingerprint> ComputeAsync(
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ContentFingerprint
            {
                Algorithm = "SHA256",
                Value = "test-fingerprint"
            });
    }

    private sealed class RecordingContentStore :
        IArtifactContentStore
    {
        public List<ArtifactId> Written { get; } = [];
        public List<ArtifactId> Deleted { get; } = [];

        public bool FailDelete { get; init; }

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

            if (FailDelete)
                throw new InvalidOperationException("forced cleanup failure");

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingRepository :
        IEvidenceRepository
    {
        public List<Artifact> Persisted { get; } = [];
        public bool FailPersistence { get; init; }

        public Task AddArtifactWithProvenanceAndRelationshipsAsync(
            Artifact artifact,
            Provenance provenance,
            IReadOnlyCollection<Relationship> relationships,
            CancellationToken cancellationToken = default)
        {
            if (FailPersistence)
                throw new InvalidOperationException("forced persistence failure");

            Persisted.Add(artifact);
            return Task.CompletedTask;
        }

        public Task AddArtifactAsync(
            Artifact artifact,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddRelationshipAsync(
            Relationship relationship,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddProvenanceAsync(
            Provenance provenance,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Artifact?> GetArtifactAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Relationship>> GetRelationshipsAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Artifact?> FindArtifactAsync(
            string source,
            ContentFingerprint fingerprint,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Artifact?>(null);

        public Task AddArtifactWithProvenanceAsync(
            Artifact artifact,
            Provenance provenance,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();


        public Task<EvidenceAggregate?> GetEvidenceAggregateAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task MergeArtifactMetadataAsync(
            ArtifactId artifactId,
            IReadOnlyDictionary<string, object> metadata,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Artifact>> GetArtifactsByMetadataAsync(
            string key,
            string value,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Provenance>> GetProvenanceAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
