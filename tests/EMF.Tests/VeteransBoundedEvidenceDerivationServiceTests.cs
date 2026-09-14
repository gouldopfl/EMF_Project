using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Core.Models.Integrity;
using EMF.Extensions.VeteransClaims.Models;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class VeteransBoundedEvidenceDerivationServiceTests
{
    [Fact]
    public async Task DeriveAsync_PersistsNeutralTraceableBoundedEvidence()
    {
        var repository =
            new TestInfrastructure.InMemoryEvidenceRepository();
        var store = new RecordingContentStore();
        var parentId = new ArtifactId("blue-button-001");

        await AddParentAsync(repository, parentId);

        var service = CreateService(repository, store);

        var result =
            await service.DeriveAsync(
                parentId,
                new ClaimIssueId("issue-osa"),
                new ServiceConnectionBasisId("basis-osa-secondary"),
                [
                    new RequirementId("requirement-b"),
                    new RequirementId("requirement-a"),
                    new RequirementId("requirement-a")
                ],
                718,
                729,
                261,
                365,
                new DateOnly(2025, 11, 19),
                "C&P PROGRESS NOTE",
                "bounded opinion"u8.ToArray());

        Assert.Equal(
            new ArtifactId("bounded-evidence-001"),
            result.Artifact.Id);
        Assert.Equal(
            "veterans-bounded-evidence",
            result.Artifact.ArtifactType);
        Assert.Equal(
            "718",
            result.Artifact.Metadata[
                VeteransArtifactMetadataKeys.SourceStartPage]);
        Assert.Equal(
            "729",
            result.Artifact.Metadata[
                VeteransArtifactMetadataKeys.SourceEndPage]);
        Assert.Equal(
            "261",
            result.Artifact.Metadata[
                VeteransArtifactMetadataKeys.SourceStartLine]);
        Assert.Equal(
            "365",
            result.Artifact.Metadata[
                VeteransArtifactMetadataKeys.SourceEndLine]);
        Assert.Equal(
            "issue-osa",
            result.Artifact.Metadata[
                VeteransArtifactMetadataKeys.ClaimIssueId]);
        Assert.Equal(
            "basis-osa-secondary",
            result.Artifact.Metadata[
                VeteransArtifactMetadataKeys.ServiceConnectionBasisId]);
        Assert.Equal(
            new[] { "requirement-a", "requirement-b" },
            Assert.IsType<string[]>(
                result.Artifact.Metadata[
                    VeteransArtifactMetadataKeys.RequirementIds]));
        Assert.Equal(
            "2025-11-19",
            result.Artifact.Metadata[
                VeteransArtifactMetadataKeys.EvidenceDate]);
        Assert.Equal(
            "C&P PROGRESS NOTE",
            result.Artifact.Metadata[
                VeteransArtifactMetadataKeys.EvidenceTitle]);

        Assert.DoesNotContain(
            "Supporting",
            result.Artifact.Metadata.Values);
        Assert.DoesNotContain(
            "Contradicting",
            result.Artifact.Metadata.Values);

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
        Assert.False(result.AlreadyExisted);
    }

    [Fact]
    public async Task DeriveAsync_ReusesExistingBoundedEvidence()
    {
        var repository =
            new TestInfrastructure.InMemoryEvidenceRepository();
        var store = new RecordingContentStore();
        var parentId = new ArtifactId("blue-button-reuse");

        await AddParentAsync(repository, parentId);

        var existingId = new ArtifactId("existing-bounded-evidence");
        var source =
            "blue-button-reuse/bounded-evidence/issue-osa/" +
            "basis-osa-secondary/10-12/20-40/requirement-a";

        await repository
            .AddArtifactWithProvenanceAndRelationshipsAsync(
                new Artifact
                {
                    Id = existingId,
                    Name = "bounded-evidence-10-12-20-40.txt",
                    ArtifactType = "veterans-bounded-evidence",
                    Fingerprint = new ContentFingerprint
                    {
                        Algorithm = "SHA256",
                        Value = "bounded-evidence-fingerprint"
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

        var service = CreateService(repository, store);

        var result =
            await service.DeriveAsync(
                parentId,
                new ClaimIssueId("issue-osa"),
                new ServiceConnectionBasisId("basis-osa-secondary"),
                [new RequirementId("requirement-a")],
                10,
                12,
                20,
                40,
                new DateOnly(2025, 1, 2),
                "C&P PROGRESS NOTE",
                "same bounded content"u8.ToArray());

        Assert.Equal(existingId, result.Artifact.Id);
        Assert.True(result.AlreadyExisted);
        Assert.Empty(store.Written);
        Assert.Equal(2, result.Relationships.Count);
    }

    [Fact]
    public async Task DeriveAsync_RejectsEmptyRequirementSetBeforeWrite()
    {
        var repository =
            new TestInfrastructure.InMemoryEvidenceRepository();
        var store = new RecordingContentStore();
        var parentId = new ArtifactId("blue-button-empty-requirements");

        await AddParentAsync(repository, parentId);

        var service = CreateService(repository, store);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.DeriveAsync(
                parentId,
                new ClaimIssueId("issue-osa"),
                new ServiceConnectionBasisId("basis-osa-secondary"),
                [],
                1,
                1,
                1,
                2,
                new DateOnly(2025, 1, 1),
                "C&P PROGRESS NOTE",
                "content"u8.ToArray()));

        Assert.Empty(store.Written);
    }

    [Fact]
    public async Task DeriveAsync_RejectsMissingParentBeforeWrite()
    {
        var repository =
            new TestInfrastructure.InMemoryEvidenceRepository();
        var store = new RecordingContentStore();
        var service = CreateService(repository, store);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeriveAsync(
                new ArtifactId("missing-parent"),
                new ClaimIssueId("issue-osa"),
                new ServiceConnectionBasisId("basis-osa-secondary"),
                [new RequirementId("requirement-a")],
                1,
                1,
                1,
                2,
                new DateOnly(2025, 1, 1),
                "C&P PROGRESS NOTE",
                "content"u8.ToArray()));

        Assert.Empty(store.Written);
    }

    [Fact]
    public async Task DeriveAsync_DeletesContentWhenPersistenceFails()
    {
        var inner =
            new TestInfrastructure.InMemoryEvidenceRepository();
        var repository = new ThrowingRepository(inner);
        var store = new RecordingContentStore();
        var parentId = new ArtifactId("blue-button-persist-fail");

        await AddParentAsync(inner, parentId);

        var service =
            new VeteransBoundedEvidenceDerivationService(
                repository,
                store,
                new StubFingerprintService(),
                new StubIdGenerator(),
                new ArtifactFactory());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeriveAsync(
                parentId,
                new ClaimIssueId("issue-osa"),
                new ServiceConnectionBasisId("basis-osa-secondary"),
                [new RequirementId("requirement-a")],
                1,
                2,
                5,
                20,
                new DateOnly(2025, 1, 1),
                "C&P PROGRESS NOTE",
                "bounded content"u8.ToArray()));

        Assert.Single(store.Written);
        Assert.Single(store.Deleted);
        Assert.Equal(store.Written[0].Id, store.Deleted[0]);
    }

    private static VeteransBoundedEvidenceDerivationService CreateService(
        IEvidenceRepository repository,
        RecordingContentStore store) =>
        new(
            repository,
            store,
            new StubFingerprintService(),
            new StubIdGenerator(),
            new ArtifactFactory());

    private static Task AddParentAsync(
        IEvidenceRepository repository,
        ArtifactId parentId) =>
        repository.AddArtifactWithProvenanceAsync(
            new Artifact
            {
                Id = parentId,
                Name = "VA Blue Button Report",
                ArtifactType = "file"
            },
            new Provenance
            {
                ArtifactId = parentId,
                Source = "/records/blue-button.pdf",
                RecordedBy = "EMF.Discovery"
            });

    private sealed class StubFingerprintService : IContentFingerprintService
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
                    Value = "bounded-evidence-fingerprint"
                });
    }

    private sealed class StubIdGenerator : IArtifactIdGenerator
    {
        public ArtifactId Generate() =>
            new("bounded-evidence-001");
    }

    private sealed class RecordingContentStore : IArtifactContentStore
    {
        public List<(ArtifactId Id, byte[] Content)> Written { get; } = [];

        public List<ArtifactId> Deleted { get; } = [];

        public Task WriteAsync(
            ArtifactId artifactId,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default)
        {
            Written.Add((artifactId, content.ToArray()));
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

    private sealed class ThrowingRepository : IEvidenceRepository
    {
        private readonly IEvidenceRepository _inner;

        public ThrowingRepository(IEvidenceRepository inner)
        {
            _inner = inner;
        }

        public Task<Artifact?> GetArtifactAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            _inner.GetArtifactAsync(artifactId, cancellationToken);

        public Task<Artifact?> FindArtifactAsync(
            string source,
            ContentFingerprint fingerprint,
            CancellationToken cancellationToken = default) =>
            _inner.FindArtifactAsync(source, fingerprint, cancellationToken);

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

        public Task<IReadOnlyList<Provenance>> GetProvenanceAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            _inner.GetProvenanceAsync(artifactId, cancellationToken);

        public Task<IReadOnlyList<Relationship>> GetRelationshipsAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            _inner.GetRelationshipsAsync(artifactId, cancellationToken);

        public Task AddArtifactAsync(
            Artifact artifact,
            CancellationToken cancellationToken = default) =>
            _inner.AddArtifactAsync(artifact, cancellationToken);

        public Task AddProvenanceAsync(
            Provenance provenance,
            CancellationToken cancellationToken = default) =>
            _inner.AddProvenanceAsync(provenance, cancellationToken);

        public Task AddRelationshipAsync(
            Relationship relationship,
            CancellationToken cancellationToken = default) =>
            _inner.AddRelationshipAsync(relationship, cancellationToken);

        public Task AddArtifactWithProvenanceAsync(
            Artifact artifact,
            Provenance provenance,
            CancellationToken cancellationToken = default) =>
            _inner.AddArtifactWithProvenanceAsync(
                artifact,
                provenance,
                cancellationToken);

        public Task AddArtifactWithProvenanceAndRelationshipsAsync(
            Artifact artifact,
            Provenance provenance,
            IReadOnlyCollection<Relationship> relationships,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Persistence failed.");
    }
}
