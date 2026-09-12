using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class ArtifactSupersessionServiceTests
{
    [Fact]
    public void Constructor_RequiresRepository()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ArtifactSupersessionService(null!));
    }

    [Fact]
    public async Task SupersedeAsync_PersistsRelationshipAndIsIdempotent()
    {
        var repository = new InMemoryEvidenceRepository();
        var oldId = new ArtifactId("statement-v1");
        var newId = new ArtifactId("statement-v2");

        await AddArtifactAsync(repository, oldId);
        await AddArtifactAsync(repository, newId);

        var service = new ArtifactSupersessionService(repository);

        var first = await service.SupersedeAsync(newId, oldId);
        var second = await service.SupersedeAsync(newId, oldId);

        Assert.False(first.AlreadyExisted);
        Assert.True(second.AlreadyExisted);
        Assert.Equal(newId, first.Relationship.SourceArtifactId);
        Assert.Equal(oldId, first.Relationship.TargetArtifactId);
        Assert.Equal(
            RelationshipTypes.Supersedes,
            first.Relationship.RelationshipType);

        Assert.Single(await repository.GetRelationshipsAsync(oldId));
    }

    [Fact]
    public async Task SupersedeAsync_ReusesHistoricalRelationshipAfterReplacementIsSuperseded()
    {
        var repository = new InMemoryEvidenceRepository();
        var v1 = new ArtifactId("statement-v1");
        var v2 = new ArtifactId("statement-v2");
        var v3 = new ArtifactId("statement-v3");

        await AddArtifactAsync(repository, v1);
        await AddArtifactAsync(repository, v2);
        await AddArtifactAsync(repository, v3);

        var service = new ArtifactSupersessionService(repository);
        await service.SupersedeAsync(v2, v1);
        await service.SupersedeAsync(v3, v2);

        var result = await service.SupersedeAsync(v2, v1);

        Assert.True(result.AlreadyExisted);
        Assert.Equal(v2, result.Relationship.SourceArtifactId);
        Assert.Equal(v1, result.Relationship.TargetArtifactId);
    }

    [Fact]
    public async Task SupersedeAsync_RejectsMissingReplacementArtifact()
    {
        var repository = new InMemoryEvidenceRepository();
        var oldId = new ArtifactId("statement-v1");
        var missingReplacementId = new ArtifactId("statement-v2");

        await AddArtifactAsync(repository, oldId);

        var service = new ArtifactSupersessionService(repository);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.SupersedeAsync(
                    missingReplacementId,
                    oldId));

        Assert.Contains("Replacement artifact not found", ex.Message);
    }

    [Fact]
    public async Task SupersedeAsync_RejectsMissingSupersededArtifact()
    {
        var repository = new InMemoryEvidenceRepository();
        var replacementId = new ArtifactId("statement-v2");
        var missingOldId = new ArtifactId("statement-v1");

        await AddArtifactAsync(repository, replacementId);

        var service = new ArtifactSupersessionService(repository);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.SupersedeAsync(
                    replacementId,
                    missingOldId));

        Assert.Contains("Superseded artifact not found", ex.Message);
    }

    [Fact]
    public async Task SupersedeAsync_RejectsSelfSupersession()
    {
        var repository = new InMemoryEvidenceRepository();
        var id = new ArtifactId("statement-v1");
        await AddArtifactAsync(repository, id);

        var service = new ArtifactSupersessionService(repository);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.SupersedeAsync(id, id));

        Assert.Contains("cannot supersede itself", ex.Message);
    }

    [Fact]
    public async Task SupersedeAsync_RejectsSecondReplacementForSameArtifact()
    {
        var repository = new InMemoryEvidenceRepository();
        var oldId = new ArtifactId("statement-v1");
        var firstReplacementId = new ArtifactId("statement-v2");
        var secondReplacementId = new ArtifactId("statement-v3");

        await AddArtifactAsync(repository, oldId);
        await AddArtifactAsync(repository, firstReplacementId);
        await AddArtifactAsync(repository, secondReplacementId);

        var service = new ArtifactSupersessionService(repository);
        await service.SupersedeAsync(firstReplacementId, oldId);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.SupersedeAsync(
                    secondReplacementId,
                    oldId));

        Assert.Contains(
            "superseded by a different replacement",
            ex.Message);
    }

    [Fact]
    public async Task SupersedeAsync_RejectsObsoleteReplacementArtifact()
    {
        var repository = new InMemoryEvidenceRepository();
        var oldId = new ArtifactId("statement-v1");
        var replacementId = new ArtifactId("statement-v2");
        var newestId = new ArtifactId("statement-v3");

        await AddArtifactAsync(repository, oldId);
        await AddArtifactAsync(repository, replacementId);
        await AddArtifactAsync(repository, newestId);

        await repository.AddRelationshipAsync(
            new Relationship
            {
                SourceArtifactId = newestId,
                TargetArtifactId = replacementId,
                RelationshipType = RelationshipTypes.Supersedes
            });

        var service = new ArtifactSupersessionService(repository);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.SupersedeAsync(
                    replacementId,
                    oldId));

        Assert.Contains("already been superseded", ex.Message);
    }

    [Fact]
    public async Task ResolveActiveArtifactIdAsync_FollowsSupersessionChain()
    {
        var repository = new InMemoryEvidenceRepository();
        var v1 = new ArtifactId("statement-v1");
        var v2 = new ArtifactId("statement-v2");
        var v3 = new ArtifactId("statement-v3");

        await AddArtifactAsync(repository, v1);
        await AddArtifactAsync(repository, v2);
        await AddArtifactAsync(repository, v3);

        var service = new ArtifactSupersessionService(repository);
        await service.SupersedeAsync(v2, v1);
        await service.SupersedeAsync(v3, v2);

        var active = await service.ResolveActiveArtifactIdAsync(v1);

        Assert.Equal(v3, active);
    }

    [Fact]
    public async Task ResolveActiveArtifactIdAsync_RejectsMissingReplacementArtifact()
    {
        var repository = new InMemoryEvidenceRepository();
        var oldId = new ArtifactId("statement-v1");
        var missingReplacementId = new ArtifactId("statement-v2");

        await AddArtifactAsync(repository, oldId);
        await repository.AddRelationshipAsync(
            Supersedes(missingReplacementId, oldId));

        var service = new ArtifactSupersessionService(repository);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ResolveActiveArtifactIdAsync(oldId));

        Assert.Contains("Evidence artifact not found", ex.Message);
        Assert.Contains(missingReplacementId.Value, ex.Message);
    }

    [Fact]
    public async Task ResolveActiveArtifactIdAsync_RejectsAmbiguousReplacement()
    {
        var repository = new InMemoryEvidenceRepository();
        var oldId = new ArtifactId("statement-v1");
        var replacement1 = new ArtifactId("statement-v2a");
        var replacement2 = new ArtifactId("statement-v2b");

        await AddArtifactAsync(repository, oldId);
        await AddArtifactAsync(repository, replacement1);
        await AddArtifactAsync(repository, replacement2);

        await repository.AddRelationshipAsync(
            Supersedes(replacement1, oldId));
        await repository.AddRelationshipAsync(
            Supersedes(replacement2, oldId));

        var service = new ArtifactSupersessionService(repository);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ResolveActiveArtifactIdAsync(oldId));

        Assert.Contains("ambiguous active", ex.Message);
    }

    [Fact]
    public async Task ResolveActiveArtifactIdAsync_RejectsMultiplePredecessors()
    {
        var repository = new InMemoryEvidenceRepository();
        var replacement = new ArtifactId("statement-v3");
        var predecessor1 = new ArtifactId("statement-v2a");
        var predecessor2 = new ArtifactId("statement-v2b");

        await AddArtifactAsync(repository, replacement);
        await AddArtifactAsync(repository, predecessor1);
        await AddArtifactAsync(repository, predecessor2);

        await repository.AddRelationshipAsync(
            Supersedes(replacement, predecessor1));
        await repository.AddRelationshipAsync(
            Supersedes(replacement, predecessor2));

        var service = new ArtifactSupersessionService(repository);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ResolveActiveArtifactIdAsync(predecessor1));

        Assert.Contains("multiple supersession predecessors", ex.Message);
    }

    [Fact]
    public async Task ResolveActiveArtifactIdAsync_RejectsCycle()
    {
        var repository = new InMemoryEvidenceRepository();
        var v1 = new ArtifactId("statement-v1");
        var v2 = new ArtifactId("statement-v2");

        await AddArtifactAsync(repository, v1);
        await AddArtifactAsync(repository, v2);

        await repository.AddRelationshipAsync(Supersedes(v2, v1));
        await repository.AddRelationshipAsync(Supersedes(v1, v2));

        var service = new ArtifactSupersessionService(repository);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ResolveActiveArtifactIdAsync(v1));

        Assert.Contains("cycle", ex.Message);
    }

    private static Task AddArtifactAsync(
        InMemoryEvidenceRepository repository,
        ArtifactId artifactId) =>
        repository.AddArtifactAsync(
            new Artifact
            {
                Id = artifactId,
                Name = $"{artifactId.Value}.pdf",
                ArtifactType = "file"
            });

    private static Relationship Supersedes(
        ArtifactId replacement,
        ArtifactId superseded) =>
        new()
        {
            SourceArtifactId = replacement,
            TargetArtifactId = superseded,
            RelationshipType = RelationshipTypes.Supersedes
        };
}
