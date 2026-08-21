using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Core.Models.Integrity;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class EvidenceRepositoryTests
{

    [Fact]
    public async Task FindArtifactAsync_MatchesSourceAndFingerprint()
    {
        var repository = new InMemoryEvidenceRepository();

        var artifact = new Artifact
        {
            Id = new ArtifactId("artifact-find-001"),
            Name = "evidence.db",
            ArtifactType = "file",
            Fingerprint = new ContentFingerprint
            {
                Algorithm = "SHA-256",
                Value = "ABC123"
            }
        };

        await repository.AddArtifactWithProvenanceAsync(
            artifact,
            new Provenance
            {
                ArtifactId = artifact.Id,
                Source = "/data/evidence.db",
                RecordedBy = "EMF.Tests"
            });

        var result = await repository.FindArtifactAsync(
            "/data/evidence.db",
            artifact.Fingerprint);

        Assert.NotNull(result);
        Assert.Equal(artifact.Id, result!.Id);
    }


    [Fact]
    public async Task Repository_StoresCompleteEvidenceAggregate()
    {
        var repository = new InMemoryEvidenceRepository();
        var id = new ArtifactId("generated-001");

        var artifact = new Artifact
        {
            Id = id,
            Name = "Generated summary",
            ArtifactType = "intelligence-output"
        };

        await repository.AddArtifactWithProvenanceAndRelationshipsAsync(
            artifact,
            new Provenance
            {
                ArtifactId = id,
                Source = "EMF.Intelligence",
                RecordedBy = "laboratory-steward"
            },
            [
                new Relationship
                {
                    SourceArtifactId = id,
                    TargetArtifactId = new ArtifactId("source-001"),
                    RelationshipType = RelationshipTypes.GeneratedFrom
                }
            ]);

        Assert.NotNull(await repository.GetArtifactAsync(id));
        Assert.Single(await repository.GetProvenanceAsync(id));
        Assert.Single(await repository.GetRelationshipsAsync(id));
    }

    [Fact]
    public async Task Repository_StoresAndRetrievesArtifact()
    {
        var repository = new InMemoryEvidenceRepository();

        var artifact = new Artifact
        {
            Id = new ArtifactId("artifact-001"),
            Name = "oscar.db",
            ArtifactType = "file"
        };

        await repository.AddArtifactAsync(artifact);

        var result = await repository.GetArtifactAsync(
            artifact.Id);

        Assert.NotNull(result);
        Assert.Equal(
            "oscar.db",
            result!.Name);
    }

    [Fact]
    public async Task Repository_StoresAndRetrievesRelationships()
    {
        var repository = new InMemoryEvidenceRepository();

        var relationship = new Relationship
        {
            SourceArtifactId = new ArtifactId("archive"),
            TargetArtifactId = new ArtifactId("database"),
            RelationshipType = RelationshipTypes.Contains
        };

        await repository.AddRelationshipAsync(relationship);

        var results = await repository.GetRelationshipsAsync(
            relationship.SourceArtifactId);

        Assert.Single(results);

        Assert.Equal(
            RelationshipTypes.Contains,
            results[0].RelationshipType);
    }

    [Fact]
    public async Task SqliteEvidenceRepository_ConcurrentDuplicatePersistence_does_not_create_duplicate_evidence()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"emf-evidence-concurrency-{Guid.NewGuid():N}.db");

        try
        {
            var repository =
                new EMF.Persistence.Repositories.SqliteEvidenceRepository(
                    databasePath);

            await repository.InitializeAsync();

            var fingerprint = new ContentFingerprint
            {
                Algorithm = "SHA-256",
                Value = "CONCURRENT-DUPLICATE-001"
            };

            var artifact1 = new Artifact
            {
                Id = new ArtifactId("artifact-concurrent-001"),
                Name = "evidence-1.db",
                ArtifactType = "file",
                Fingerprint = fingerprint
            };

            var artifact2 = new Artifact
            {
                Id = new ArtifactId("artifact-concurrent-002"),
                Name = "evidence-2.db",
                ArtifactType = "file",
                Fingerprint = fingerprint
            };

            var task1 =
                repository.AddArtifactWithProvenanceAsync(
                    artifact1,
                    new Provenance
                    {
                        ArtifactId = artifact1.Id,
                        Source = "/data/concurrent.db",
                        RecordedBy = "EMF.Tests"
                    });

            var task2 =
                repository.AddArtifactWithProvenanceAsync(
                    artifact2,
                    new Provenance
                    {
                        ArtifactId = artifact2.Id,
                        Source = "/data/concurrent.db",
                        RecordedBy = "EMF.Tests"
                    });

            await Task.WhenAll(task1, task2);

            var first =
                await repository.FindArtifactAsync(
                    "/data/concurrent.db",
                    fingerprint);

            Assert.NotNull(first);

            var stored1 =
                await repository.GetArtifactAsync(artifact1.Id);

            var stored2 =
                await repository.GetArtifactAsync(artifact2.Id);

            Assert.True(
                (stored1 is not null) ^ (stored2 is not null));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }


    [Fact]
    public async Task Repository_GetsArtifactsByMetadata()
    {
        var repository = new InMemoryEvidenceRepository();

        var first = new Artifact
        {
            Id = new ArtifactId("artifact-meta-001"),
            Name = "first",
            ArtifactType = "intelligence-output",
            CreatedUtc = new DateTimeOffset(
                2026, 8, 20, 20, 0, 0,
                TimeSpan.Zero),
            Metadata = new Dictionary<string, object>
            {
                ["evidenceGapId"] = "gap-1"
            }
        };

        var second = new Artifact
        {
            Id = new ArtifactId("artifact-meta-002"),
            Name = "second",
            ArtifactType = "intelligence-output",
            CreatedUtc = new DateTimeOffset(
                2026, 8, 20, 20, 0, 1,
                TimeSpan.Zero),
            Metadata = new Dictionary<string, object>
            {
                ["evidenceGapId"] = "gap-1"
            }
        };

        var other = new Artifact
        {
            Id = new ArtifactId("artifact-meta-003"),
            Name = "other",
            ArtifactType = "intelligence-output",
            Metadata = new Dictionary<string, object>
            {
                ["evidenceGapId"] = "gap-2"
            }
        };

        await repository.AddArtifactAsync(first);
        await repository.AddArtifactAsync(second);
        await repository.AddArtifactAsync(other);

        var results =
            await repository.GetArtifactsByMetadataAsync(
                "evidenceGapId",
                "gap-1");

        Assert.Equal(2, results.Count);
        Assert.Equal(first.Id, results[0].Id);
        Assert.Equal(second.Id, results[1].Id);
    }

    [Fact]
    public async Task SqliteEvidenceRepository_GetsArtifactsByMetadata()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"emf-evidence-metadata-{Guid.NewGuid():N}.db");

        try
        {
            var repository =
                new EMF.Persistence.Repositories.SqliteEvidenceRepository(
                    databasePath);

            await repository.InitializeAsync();

            var first = new Artifact
            {
                Id = new ArtifactId("sqlite-meta-001"),
                Name = "first",
                ArtifactType = "intelligence-output",
                CreatedUtc = new DateTimeOffset(
                    2026, 8, 20, 21, 0, 0,
                    TimeSpan.Zero),
                Metadata = new Dictionary<string, object>
                {
                    ["requirementId"] = "req-1"
                }
            };

            var second = new Artifact
            {
                Id = new ArtifactId("sqlite-meta-002"),
                Name = "second",
                ArtifactType = "intelligence-output",
                CreatedUtc = new DateTimeOffset(
                    2026, 8, 20, 21, 0, 1,
                    TimeSpan.Zero),
                Metadata = new Dictionary<string, object>
                {
                    ["requirementId"] = "req-1"
                }
            };

            var other = new Artifact
            {
                Id = new ArtifactId("sqlite-meta-003"),
                Name = "other",
                ArtifactType = "intelligence-output",
                Metadata = new Dictionary<string, object>
                {
                    ["requirementId"] = "req-2"
                }
            };

            await repository.AddArtifactAsync(first);
            await repository.AddArtifactAsync(second);
            await repository.AddArtifactAsync(other);

            var results =
                await repository.GetArtifactsByMetadataAsync(
                    "requirementId",
                    "req-1");

            Assert.Equal(2, results.Count);
            Assert.Equal(first.Id, results[0].Id);
            Assert.Equal(second.Id, results[1].Id);
            Assert.Equal(
                "req-1",
                results[0].Metadata["requirementId"].ToString());
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

}
