using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Core.Models.Integrity;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class EvidenceFileIngestionServiceTests
{
    [Fact]
    public async Task IngestAsync_PersistsFile()
    {
        var path = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(
                path,
                "evidence content");

            var repository = new RecordingRepository();
            var store = new RecordingContentStore();

            var service =
                new EvidenceFileIngestionService(
                    repository,
                    store,
                    new StubFingerprintService(),
                    new StubIdGenerator(),
                    new ArtifactFactory());

            var result =
                await service.IngestAsync(path);

            Assert.Equal(
                Path.GetFileName(path),
                result.Artifact.Name);

            Assert.Equal(
                "file",
                result.Artifact.ArtifactType);

            Assert.False(result.AlreadyExisted);
            Assert.Single(repository.Persisted);
            Assert.Single(store.Written);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task IngestAsync_DeletesContentWhenPersistenceFails()
    {
        var path = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(
                path,
                "evidence content");

            var repository =
                new RecordingRepository
                {
                    FailPersistence = true
                };

            var store = new RecordingContentStore();

            var service =
                new EvidenceFileIngestionService(
                    repository,
                    store,
                    new StubFingerprintService(),
                    new StubIdGenerator(),
                    new ArtifactFactory());

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.IngestAsync(path));

            Assert.Single(store.Deleted);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task IngestAsync_AggregatesCleanupFailure()
    {
        var path = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(
                path,
                "evidence content");

            var repository =
                new RecordingRepository
                {
                    FailPersistence = true
                };

            var store =
                new RecordingContentStore
                {
                    FailDelete = true
                };

            var service =
                new EvidenceFileIngestionService(
                    repository,
                    store,
                    new StubFingerprintService(),
                    new StubIdGenerator(),
                    new ArtifactFactory());

            var exception =
                await Assert.ThrowsAsync<AggregateException>(
                    () => service.IngestAsync(path));

            Assert.Equal(
                2,
                exception.InnerExceptions.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task IngestAsync_ReusesExistingFile()
    {
        var path = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(
                path,
                "evidence content");

            var fullPath = Path.GetFullPath(path);

            var existing =
                new Artifact
                {
                    Id = new ArtifactId("existing-evidence"),
                    Name = Path.GetFileName(path),
                    ArtifactType = "file",
                    Fingerprint =
                        new ContentFingerprint
                        {
                            Algorithm = "SHA256",
                            Value = "test-fingerprint"
                        }
                };

            var repository =
                new RecordingRepository
                {
                    ExistingArtifact = existing,
                    ExistingProvenance =
                        new Provenance
                        {
                            ArtifactId = existing.Id,
                            Source = fullPath,
                            RecordedBy = "EMF.Discovery"
                        }
                };

            var store = new RecordingContentStore();

            var service =
                new EvidenceFileIngestionService(
                    repository,
                    store,
                    new StubFingerprintService(),
                    new StubIdGenerator(),
                    new ArtifactFactory());

            var result =
                await service.IngestAsync(path);

            Assert.Equal(
                existing.Id,
                result.Artifact.Id);

            Assert.True(result.AlreadyExisted);
            Assert.Empty(store.Written);
            Assert.Empty(repository.Persisted);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class RecordingRepository :
        IEvidenceRepository
    {
        public List<Artifact> Persisted { get; } = [];

        public bool FailPersistence { get; init; }

        public Artifact? ExistingArtifact { get; init; }

        public Provenance? ExistingProvenance { get; init; }

        public Task AddArtifactWithProvenanceAsync(
            Artifact artifact,
            Provenance provenance,
            CancellationToken cancellationToken = default)
        {
            if (FailPersistence)
            {
                throw new InvalidOperationException(
                    "forced persistence failure");
            }

            Persisted.Add(artifact);
            return Task.CompletedTask;
        }

        public Task<Artifact?> FindArtifactAsync(
            string source,
            ContentFingerprint fingerprint,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ExistingArtifact);

        public Task<IReadOnlyList<Provenance>> GetProvenanceAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Provenance>>(
                ExistingProvenance is null
                    ? []
                    : [ExistingProvenance]);

        public Task AddArtifactAsync(
            Artifact artifact,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddRelationshipAsync(
            Relationship relationship,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Artifact?> GetArtifactAsync(
            ArtifactId artifactId,
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

        public Task MergeArtifactMetadataAsync(
            ArtifactId artifactId,
            IReadOnlyDictionary<string, object> metadata,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Relationship>> GetRelationshipsAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddProvenanceAsync(
            Provenance provenance,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddArtifactWithProvenanceAndRelationshipsAsync(
            Artifact artifact,
            Provenance provenance,
            IReadOnlyCollection<Relationship> relationships,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
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
            {
                throw new InvalidOperationException(
                    "forced cleanup failure");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class StubIdGenerator :
        IArtifactIdGenerator
    {
        public ArtifactId Generate() =>
            new("evidence-001");
    }

    private sealed class StubFingerprintService :
        IContentFingerprintService
    {
        public Task<ContentFingerprint> ComputeAsync(
            string sourcePath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new ContentFingerprint
                {
                    Algorithm = "SHA256",
                    Value = "test-fingerprint"
                });

        public Task<ContentFingerprint> ComputeAsync(
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
