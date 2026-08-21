using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class EvidenceLineageServiceTests
{
    [Fact]
    public async Task GetGeneratedFromAncestorsAsync_ReturnsDirectSource()
    {
        var repository = new InMemoryEvidenceRepository();
        var service = new EvidenceLineageService(repository);

        var generated = new Artifact
        {
            Id = new ArtifactId("generated-001"),
            Name = "Generated",
            ArtifactType = "intelligence-output"
        };

        var source = new Artifact
        {
            Id = new ArtifactId("source-001"),
            Name = "Source",
            ArtifactType = "file"
        };

        await repository.AddArtifactAsync(generated);
        await repository.AddArtifactAsync(source);

        await repository.AddRelationshipAsync(
            new Relationship
            {
                SourceArtifactId = generated.Id,
                TargetArtifactId = source.Id,
                RelationshipType = RelationshipTypes.GeneratedFrom
            });

        var result =
            await service.GetGeneratedFromAncestorsAsync(generated.Id);

        var ancestor = Assert.Single(result);
        Assert.Equal(source.Id, ancestor.Id);
    }
    [Fact]
    public async Task GetGeneratedFromAncestorsAsync_ReturnsRecursiveAncestors()
    {
        var repository = new InMemoryEvidenceRepository();
        var service = new EvidenceLineageService(repository);

        var generated = new Artifact
        {
            Id = new ArtifactId("generated-002"),
            Name = "Generated",
            ArtifactType = "intelligence-output"
        };

        var intermediate = new Artifact
        {
            Id = new ArtifactId("intermediate-002"),
            Name = "Intermediate",
            ArtifactType = "intelligence-output"
        };

        var source = new Artifact
        {
            Id = new ArtifactId("source-002"),
            Name = "Source",
            ArtifactType = "file"
        };

        await repository.AddArtifactAsync(generated);
        await repository.AddArtifactAsync(intermediate);
        await repository.AddArtifactAsync(source);

        await repository.AddRelationshipAsync(
            new Relationship
            {
                SourceArtifactId = generated.Id,
                TargetArtifactId = intermediate.Id,
                RelationshipType = RelationshipTypes.GeneratedFrom
            });

        await repository.AddRelationshipAsync(
            new Relationship
            {
                SourceArtifactId = intermediate.Id,
                TargetArtifactId = source.Id,
                RelationshipType = RelationshipTypes.GeneratedFrom
            });

        var result =
            await service.GetGeneratedFromAncestorsAsync(generated.Id);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, artifact => artifact.Id == intermediate.Id);
        Assert.Contains(result, artifact => artifact.Id == source.Id);
    }

    [Fact]
    public async Task GetGeneratedFromAncestorsAsync_IgnoresCycles()
    {
        var repository = new InMemoryEvidenceRepository();
        var service = new EvidenceLineageService(repository);

        var first = new Artifact
        {
            Id = new ArtifactId("cycle-001"),
            Name = "First",
            ArtifactType = "intelligence-output"
        };

        var second = new Artifact
        {
            Id = new ArtifactId("cycle-002"),
            Name = "Second",
            ArtifactType = "intelligence-output"
        };

        await repository.AddArtifactAsync(first);
        await repository.AddArtifactAsync(second);

        await repository.AddRelationshipAsync(
            new Relationship
            {
                SourceArtifactId = first.Id,
                TargetArtifactId = second.Id,
                RelationshipType = RelationshipTypes.GeneratedFrom
            });

        await repository.AddRelationshipAsync(
            new Relationship
            {
                SourceArtifactId = second.Id,
                TargetArtifactId = first.Id,
                RelationshipType = RelationshipTypes.GeneratedFrom
            });

        var result =
            await service.GetGeneratedFromAncestorsAsync(first.Id);

        var ancestor = Assert.Single(result);
        Assert.Equal(second.Id, ancestor.Id);
    }

}
