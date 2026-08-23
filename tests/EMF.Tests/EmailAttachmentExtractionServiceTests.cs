using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Core.Models.Integrity;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class EmailAttachmentExtractionServiceTests
{
    [Fact]
    public async Task ExtractAsync_PersistsAttachment()
    {
        var repository = new RecordingRepository();
        var store = new RecordingContentStore();
        var service = CreateService(repository, store);

        var result = await service.ExtractAsync(
            new ArtifactId("email-001"),
            "medical-record.pdf",
            "application/pdf",
            "attachment bytes"u8.ToArray());

        Assert.Equal("medical-record.pdf", result.Artifact.Name);
        Assert.Equal("email-attachment", result.Artifact.ArtifactType);
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
                new ArtifactId("email-002"),
                "record.txt",
                "text/plain",
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
                new ArtifactId("email-003"),
                "record.txt",
                "text/plain",
                "content"u8.ToArray()));

        Assert.Equal(2, exception.InnerExceptions.Count);
    }

    private static EmailAttachmentExtractionService CreateService(
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
            throw new NotSupportedException();

        public Task AddArtifactWithProvenanceAsync(
            Artifact artifact,
            Provenance provenance,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();


        public Task<EvidenceAggregate?> GetEvidenceAggregateAsync(
            ArtifactId artifactId,
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
