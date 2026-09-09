using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Core.Models.Integrity;
using EMF.Extensions.VeteransClaims.Models;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class VeteransClinicalNoteDerivationServiceTests
{
    [Fact]
    public async Task DeriveAsync_PersistsTraceableClinicalNote()
    {
        var repository =
            new TestInfrastructure.InMemoryEvidenceRepository();
        var store = new RecordingContentStore();

        var parentId = new ArtifactId("parent-001");

        await repository.AddArtifactWithProvenanceAsync(
            new Artifact
            {
                Id = parentId,
                Name = "parent.pdf",
                ArtifactType = "file"
            },
            new Provenance
            {
                ArtifactId = parentId,
                Source = "/records/parent.pdf",
                RecordedBy = "EMF.Discovery"
            });

        var service =
            new VeteransClinicalNoteDerivationService(
                repository,
                store,
                new StubFingerprintService(),
                new StubIdGenerator(),
                new ArtifactFactory());

        var result =
            await service.DeriveAsync(
                parentId,
                "sleep-note.txt",
                1003,
                1005,
                new DateOnly(2025, 7, 30),
                "PAP SET-UP CONSULT",
                "clinical note"u8.ToArray());

        Assert.Equal(
            new ArtifactId("clinical-note-001"),
            result.Artifact.Id);
        Assert.Equal(
            "veterans-clinical-note",
            result.Artifact.ArtifactType);

        Assert.Equal(
            "1003",
            result.Artifact.Metadata[
                VeteransArtifactMetadataKeys.SourceStartPage]);
        Assert.Equal(
            "1005",
            result.Artifact.Metadata[
                VeteransArtifactMetadataKeys.SourceEndPage]);
        Assert.Equal(
            "2025-07-30",
            result.Artifact.Metadata[
                VeteransArtifactMetadataKeys.NoteDate]);
        Assert.Equal(
            "PAP SET-UP CONSULT",
            result.Artifact.Metadata[
                VeteransArtifactMetadataKeys.NoteTitle]);

        Assert.Equal(2, result.Relationships.Count);

        Assert.Contains(
            result.Relationships,
            x =>
                x.SourceArtifactId == parentId &&
                x.TargetArtifactId == result.Artifact.Id &&
                x.RelationshipType == RelationshipTypes.Contains);

        Assert.Contains(
            result.Relationships,
            x =>
                x.SourceArtifactId == result.Artifact.Id &&
                x.TargetArtifactId == parentId &&
                x.RelationshipType == RelationshipTypes.DerivedFrom);

        Assert.Single(store.Written);
    }

    [Fact]
    public async Task DeriveAsync_RejectsMissingParentBeforeWrite()
    {
        var repository =
            new TestInfrastructure.InMemoryEvidenceRepository();
        var store = new RecordingContentStore();

        var service =
            new VeteransClinicalNoteDerivationService(
                repository,
                store,
                new StubFingerprintService(),
                new StubIdGenerator(),
                new ArtifactFactory());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeriveAsync(
                new ArtifactId("missing-parent"),
                "note.txt",
                10,
                11,
                new DateOnly(2025, 1, 1),
                "Sleep Note",
                "content"u8.ToArray()));

        Assert.Empty(store.Written);
    }

    [Fact]
    public async Task DeriveAsync_ReusesExistingClinicalNote()
    {
        var repository =
            new TestInfrastructure.InMemoryEvidenceRepository();
        var store = new RecordingContentStore();
        var parentId = new ArtifactId("parent-duplicate");

        await repository.AddArtifactWithProvenanceAsync(
            new Artifact
            {
                Id = parentId,
                Name = "parent.pdf",
                ArtifactType = "file"
            },
            new Provenance
            {
                ArtifactId = parentId,
                Source = "/records/parent.pdf",
                RecordedBy = "EMF.Discovery"
            });

        var existingId = new ArtifactId("existing-note");
        var source =
            "parent-duplicate/clinical-note/20-21/note.txt";

        await repository
            .AddArtifactWithProvenanceAndRelationshipsAsync(
                new Artifact
                {
                    Id = existingId,
                    Name = "note.txt",
                    ArtifactType = "veterans-clinical-note",
                    Fingerprint = new ContentFingerprint
                    {
                        Algorithm = "SHA256",
                        Value = "clinical-note-fingerprint"
                    }
                },
                new Provenance
                {
                    ArtifactId = existingId,
                    Source = source,
                    RecordedBy = "EMF.Discovery"
                },
                [
                    new Relationship
                    {
                        SourceArtifactId = parentId,
                        TargetArtifactId = existingId,
                        RelationshipType = RelationshipTypes.Contains
                    },
                    new Relationship
                    {
                        SourceArtifactId = existingId,
                        TargetArtifactId = parentId,
                        RelationshipType = RelationshipTypes.DerivedFrom
                    }
                ]);

        var service =
            new VeteransClinicalNoteDerivationService(
                repository,
                store,
                new StubFingerprintService(),
                new StubIdGenerator(),
                new ArtifactFactory());

        var result =
            await service.DeriveAsync(
                parentId,
                "note.txt",
                20,
                21,
                new DateOnly(2025, 1, 2),
                "Sleep Note",
                "same content"u8.ToArray());

        Assert.Equal(existingId, result.Artifact.Id);
        Assert.Empty(store.Written);
        Assert.Equal(2, result.Relationships.Count);
    }

    [Fact]
    public async Task DeriveAsync_DeletesContentWhenPersistenceFails()
    {
        var inner =
            new TestInfrastructure.InMemoryEvidenceRepository();
        var parentId = new ArtifactId("parent-failure");

        await inner.AddArtifactWithProvenanceAsync(
            new Artifact
            {
                Id = parentId,
                Name = "parent.pdf",
                ArtifactType = "file"
            },
            new Provenance
            {
                ArtifactId = parentId,
                Source = "/records/parent.pdf",
                RecordedBy = "EMF.Discovery"
            });

        var store = new RecordingContentStore();
        var service =
            new VeteransClinicalNoteDerivationService(
                new FailingPersistenceRepository(inner),
                store,
                new StubFingerprintService(),
                new StubIdGenerator(),
                new ArtifactFactory());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeriveAsync(
                parentId,
                "note.txt",
                30,
                31,
                new DateOnly(2025, 1, 3),
                "Sleep Note",
                "content"u8.ToArray()));

        Assert.Single(store.Written);
        Assert.Single(store.Deleted);
        Assert.Equal(store.Written[0], store.Deleted[0]);
    }

    [Fact]
    public async Task DeriveAsync_RejectsEmptyContentBeforeWrite()
    {
        var repository =
            new TestInfrastructure.InMemoryEvidenceRepository();
        var store = new RecordingContentStore();
        var parentId = new ArtifactId("parent-empty");

        await repository.AddArtifactWithProvenanceAsync(
            new Artifact
            {
                Id = parentId,
                Name = "parent.pdf",
                ArtifactType = "file"
            },
            new Provenance
            {
                ArtifactId = parentId,
                Source = "/records/parent.pdf",
                RecordedBy = "EMF.Discovery"
            });

        var service =
            new VeteransClinicalNoteDerivationService(
                repository,
                store,
                new StubFingerprintService(),
                new StubIdGenerator(),
                new ArtifactFactory());

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.DeriveAsync(
                parentId,
                "note.txt",
                30,
                31,
                new DateOnly(2025, 1, 3),
                "Sleep Note",
                ReadOnlyMemory<byte>.Empty));

        Assert.Empty(store.Written);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(10, 9)]
    public async Task DeriveAsync_RejectsInvalidPageRange(
        int startPage,
        int endPage)
    {
        var repository =
            new TestInfrastructure.InMemoryEvidenceRepository();
        var store = new RecordingContentStore();
        var parentId = new ArtifactId("parent-range");

        await repository.AddArtifactWithProvenanceAsync(
            new Artifact
            {
                Id = parentId,
                Name = "parent.pdf",
                ArtifactType = "file"
            },
            new Provenance
            {
                ArtifactId = parentId,
                Source = "/records/parent.pdf",
                RecordedBy = "EMF.Discovery"
            });

        var service =
            new VeteransClinicalNoteDerivationService(
                repository,
                store,
                new StubFingerprintService(),
                new StubIdGenerator(),
                new ArtifactFactory());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.DeriveAsync(
                parentId,
                "note.txt",
                startPage,
                endPage,
                new DateOnly(2025, 1, 4),
                "Sleep Note",
                "content"u8.ToArray()));

        Assert.Empty(store.Written);
    }

    [Fact]
    public async Task DeriveAsync_RejectsNullFactoryResultBeforeWrite()
    {
        var repository =
            new TestInfrastructure.InMemoryEvidenceRepository();
        var store = new RecordingContentStore();
        var parentId = new ArtifactId("parent-factory");

        await repository.AddArtifactWithProvenanceAsync(
            new Artifact
            {
                Id = parentId,
                Name = "parent.pdf",
                ArtifactType = "file"
            },
            new Provenance
            {
                ArtifactId = parentId,
                Source = "/records/parent.pdf",
                RecordedBy = "EMF.Discovery"
            });

        var service =
            new VeteransClinicalNoteDerivationService(
                repository,
                store,
                new StubFingerprintService(),
                new StubIdGenerator(),
                new NullFactory());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeriveAsync(
                parentId,
                "note.txt",
                40,
                41,
                new DateOnly(2025, 1, 5),
                "Sleep Note",
                "content"u8.ToArray()));

        Assert.Empty(store.Written);
    }

    private sealed class NullFactory : IArtifactFactory
    {
        public EMF.Orchestration.Models.ArtifactCreationResult Create(
            EMF.Discovery.Models.DiscoveredItem item,
            ArtifactId artifactId,
            ContentFingerprint? fingerprint) =>
            null!;
    }

    [Fact]
    public async Task DeriveAsync_RejectsFactoryFingerprintMismatch()
    {
        var repository =
            new TestInfrastructure.InMemoryEvidenceRepository();
        var store = new RecordingContentStore();
        var parentId = new ArtifactId("parent-fingerprint");

        await repository.AddArtifactWithProvenanceAsync(
            new Artifact
            {
                Id = parentId,
                Name = "parent.pdf",
                ArtifactType = "file"
            },
            new Provenance
            {
                ArtifactId = parentId,
                Source = "/records/parent.pdf",
                RecordedBy = "EMF.Discovery"
            });

        var service =
            new VeteransClinicalNoteDerivationService(
                repository,
                store,
                new StubFingerprintService(),
                new StubIdGenerator(),
                new WrongFingerprintFactory());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeriveAsync(
                parentId,
                "note.txt",
                50,
                51,
                new DateOnly(2025, 1, 6),
                "Sleep Note",
                "content"u8.ToArray()));

        Assert.Empty(store.Written);
    }

    private sealed class WrongFingerprintFactory : IArtifactFactory
    {
        public EMF.Orchestration.Models.ArtifactCreationResult Create(
            EMF.Discovery.Models.DiscoveredItem item,
            ArtifactId artifactId,
            ContentFingerprint? fingerprint) =>
            new ArtifactFactory().Create(
                item,
                artifactId,
                new ContentFingerprint
                {
                    Algorithm = "SHA256",
                    Value = "wrong-fingerprint"
                });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeriveAsync_RejectsInvalidFactoryProvenance(
        bool wrongSource)
    {
        var repository =
            new TestInfrastructure.InMemoryEvidenceRepository();
        var store = new RecordingContentStore();
        var parentId = new ArtifactId("parent-provenance");

        await repository.AddArtifactWithProvenanceAsync(
            new Artifact
            {
                Id = parentId,
                Name = "parent.pdf",
                ArtifactType = "file"
            },
            new Provenance
            {
                ArtifactId = parentId,
                Source = "/records/parent.pdf",
                RecordedBy = "EMF.Discovery"
            });

        var service =
            new VeteransClinicalNoteDerivationService(
                repository,
                store,
                new StubFingerprintService(),
                new StubIdGenerator(),
                new WrongProvenanceFactory(wrongSource));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeriveAsync(
                parentId,
                "note.txt",
                60,
                61,
                new DateOnly(2025, 1, 7),
                "Sleep Note",
                "content"u8.ToArray()));

        Assert.Empty(store.Written);
    }

    private sealed class WrongProvenanceFactory : IArtifactFactory
    {
        private readonly bool _wrongSource;

        public WrongProvenanceFactory(bool wrongSource)
        {
            _wrongSource = wrongSource;
        }

        public EMF.Orchestration.Models.ArtifactCreationResult Create(
            EMF.Discovery.Models.DiscoveredItem item,
            ArtifactId artifactId,
            ContentFingerprint? fingerprint)
        {
            var valid =
                new ArtifactFactory().Create(
                    item,
                    artifactId,
                    fingerprint);

            return new EMF.Orchestration.Models.ArtifactCreationResult
            {
                Artifact = valid.Artifact,
                Provenance =
                    new Provenance
                    {
                        ArtifactId =
                            _wrongSource
                                ? artifactId
                                : new ArtifactId("wrong-artifact"),
                        Source =
                            _wrongSource
                                ? "wrong-source"
                                : valid.Provenance.Source,
                        RecordedBy =
                            valid.Provenance.RecordedBy
                    }
            };
        }
    }

    [Fact]
    public async Task DeriveAsync_RejectsFactoryArtifactIdentityMismatch()
    {
        var repository =
            new TestInfrastructure.InMemoryEvidenceRepository();
        var store = new RecordingContentStore();
        var parentId = new ArtifactId("parent-identity");

        await repository.AddArtifactWithProvenanceAsync(
            new Artifact
            {
                Id = parentId,
                Name = "parent.pdf",
                ArtifactType = "file"
            },
            new Provenance
            {
                ArtifactId = parentId,
                Source = "/records/parent.pdf",
                RecordedBy = "EMF.Discovery"
            });

        var service =
            new VeteransClinicalNoteDerivationService(
                repository,
                store,
                new StubFingerprintService(),
                new StubIdGenerator(),
                new WrongIdentityFactory());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeriveAsync(
                parentId,
                "note.txt",
                70,
                71,
                new DateOnly(2025, 1, 8),
                "Sleep Note",
                "content"u8.ToArray()));

        Assert.Empty(store.Written);
    }

    private sealed class WrongIdentityFactory : IArtifactFactory
    {
        public EMF.Orchestration.Models.ArtifactCreationResult Create(
            EMF.Discovery.Models.DiscoveredItem item,
            ArtifactId artifactId,
            ContentFingerprint? fingerprint) =>
            new ArtifactFactory().Create(
                item,
                new ArtifactId("wrong-artifact"),
                fingerprint);
    }

    [Fact]
    public async Task DeriveAsync_RejectsOversizedContentBeforeWrite()
    {
        var repository =
            new TestInfrastructure.InMemoryEvidenceRepository();
        var store = new RecordingContentStore();
        var parentId = new ArtifactId("parent-oversized");

        await repository.AddArtifactWithProvenanceAsync(
            new Artifact
            {
                Id = parentId,
                Name = "parent.pdf",
                ArtifactType = "file"
            },
            new Provenance
            {
                ArtifactId = parentId,
                Source = "/records/parent.pdf",
                RecordedBy = "EMF.Discovery"
            });

        var service =
            new VeteransClinicalNoteDerivationService(
                repository,
                store,
                new StubFingerprintService(),
                new StubIdGenerator(),
                new ArtifactFactory(),
                maxContentBytes: 4);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.DeriveAsync(
                parentId,
                "note.txt",
                80,
                81,
                new DateOnly(2025, 1, 9),
                "Sleep Note",
                "12345"u8.ToArray()));

        Assert.Empty(store.Written);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsInvalidMaxContentBytes(
        int maxContentBytes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new VeteransClinicalNoteDerivationService(
                new TestInfrastructure.InMemoryEvidenceRepository(),
                new RecordingContentStore(),
                new StubFingerprintService(),
                new StubIdGenerator(),
                new ArtifactFactory(),
                maxContentBytes));
    }

    private sealed class StubIdGenerator : IArtifactIdGenerator
    {
        public ArtifactId Generate() =>
            new("clinical-note-001");
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
            Task.FromResult(
                new ContentFingerprint
                {
                    Algorithm = "SHA256",
                    Value = "clinical-note-fingerprint"
                });
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

    private sealed class FailingPersistenceRepository :
        IEvidenceRepository
    {
        private readonly IEvidenceRepository _inner;

        public FailingPersistenceRepository(
            IEvidenceRepository inner)
        {
            _inner = inner;
        }

        public Task<Artifact?> GetArtifactAsync(
            ArtifactId id,
            CancellationToken c = default) =>
            _inner.GetArtifactAsync(id, c);

        public Task<Artifact?> FindArtifactAsync(
            string source,
            ContentFingerprint fingerprint,
            CancellationToken c = default) =>
            _inner.FindArtifactAsync(source, fingerprint, c);

        public Task<IReadOnlyList<Relationship>> GetRelationshipsAsync(
            ArtifactId id,
            CancellationToken c = default) =>
            _inner.GetRelationshipsAsync(id, c);

        public Task<IReadOnlyList<Provenance>> GetProvenanceAsync(
            ArtifactId id,
            CancellationToken c = default) =>
            _inner.GetProvenanceAsync(id, c);

        public Task AddArtifactWithProvenanceAndRelationshipsAsync(
            Artifact artifact,
            Provenance provenance,
            IReadOnlyCollection<Relationship> relationships,
            CancellationToken c = default) =>
            throw new InvalidOperationException(
                "forced persistence failure");

        public Task AddArtifactAsync(
            Artifact a,
            CancellationToken c = default) =>
            throw new NotSupportedException();

        public Task AddRelationshipAsync(
            Relationship r,
            CancellationToken c = default) =>
            throw new NotSupportedException();

        public Task<EvidenceAggregate?> GetEvidenceAggregateAsync(
            ArtifactId id,
            CancellationToken c = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Artifact>> GetArtifactsByMetadataAsync(
            string key,
            string value,
            CancellationToken c = default) =>
            throw new NotSupportedException();

        public Task MergeArtifactMetadataAsync(
            ArtifactId id,
            IReadOnlyDictionary<string, object> metadata,
            CancellationToken c = default) =>
            throw new NotSupportedException();

        public Task AddProvenanceAsync(
            Provenance p,
            CancellationToken c = default) =>
            throw new NotSupportedException();

        public Task AddArtifactWithProvenanceAsync(
            Artifact a,
            Provenance p,
            CancellationToken c = default) =>
            throw new NotSupportedException();
    }

}
