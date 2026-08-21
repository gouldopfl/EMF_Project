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

        Assert.Equal(
            source.Id,
            ancestor.Artifact.Id);

        Assert.Equal(
            generated.Id,
            ancestor.Relationship.SourceArtifactId);

        Assert.Equal(
            source.Id,
            ancestor.Relationship.TargetArtifactId);

        Assert.Equal(
            RelationshipTypes.GeneratedFrom,
            ancestor.Relationship.RelationshipType);
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

        var intermediateNode =
            Assert.Single(
                result.Where(
                    node =>
                        node.Artifact.Id == intermediate.Id));

        var sourceNode =
            Assert.Single(
                result.Where(
                    node =>
                        node.Artifact.Id == source.Id));

        Assert.Equal(1, intermediateNode.Depth);
        Assert.Equal(2, sourceNode.Depth);
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
        Assert.Equal(second.Id, ancestor.Artifact.Id);
    }

    [Fact]
    public async Task GetGeneratedFromAncestorsAsync_ReturnsBranchingAncestors()
    {
        var repository = new InMemoryEvidenceRepository();
        var service = new EvidenceLineageService(repository);

        var generated = new Artifact
        {
            Id = new ArtifactId("branch-generated"),
            Name = "Generated",
            ArtifactType = "intelligence-output"
        };

        var firstSource = new Artifact
        {
            Id = new ArtifactId("branch-source-1"),
            Name = "First source",
            ArtifactType = "file"
        };

        var secondSource = new Artifact
        {
            Id = new ArtifactId("branch-source-2"),
            Name = "Second source",
            ArtifactType = "file"
        };

        await repository.AddArtifactAsync(generated);
        await repository.AddArtifactAsync(firstSource);
        await repository.AddArtifactAsync(secondSource);

        await repository.AddRelationshipAsync(
            new Relationship
            {
                SourceArtifactId = generated.Id,
                TargetArtifactId = firstSource.Id,
                RelationshipType = RelationshipTypes.GeneratedFrom
            });

        await repository.AddRelationshipAsync(
            new Relationship
            {
                SourceArtifactId = generated.Id,
                TargetArtifactId = secondSource.Id,
                RelationshipType = RelationshipTypes.GeneratedFrom
            });

        var result =
            await service.GetGeneratedFromAncestorsAsync(generated.Id);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, node => node.Artifact.Id == firstSource.Id);
        Assert.Contains(result, node => node.Artifact.Id == secondSource.Id);
    }

    [Fact]
    public async Task GetGeneratedFromAncestorsAsync_IgnoresOtherRelationshipTypes()
    {
        var repository = new InMemoryEvidenceRepository();
        var service = new EvidenceLineageService(repository);

        var generated = new Artifact
        {
            Id = new ArtifactId("mixed-generated"),
            Name = "Generated",
            ArtifactType = "intelligence-output"
        };

        var source = new Artifact
        {
            Id = new ArtifactId("mixed-source"),
            Name = "Source",
            ArtifactType = "file"
        };

        var referenced = new Artifact
        {
            Id = new ArtifactId("mixed-reference"),
            Name = "Reference",
            ArtifactType = "file"
        };

        await repository.AddArtifactAsync(generated);
        await repository.AddArtifactAsync(source);
        await repository.AddArtifactAsync(referenced);

        await repository.AddRelationshipAsync(
            new Relationship
            {
                SourceArtifactId = generated.Id,
                TargetArtifactId = source.Id,
                RelationshipType = RelationshipTypes.GeneratedFrom
            });

        await repository.AddRelationshipAsync(
            new Relationship
            {
                SourceArtifactId = generated.Id,
                TargetArtifactId = referenced.Id,
                RelationshipType = RelationshipTypes.References
            });

        var result =
            await service.GetGeneratedFromAncestorsAsync(generated.Id);

        var ancestor = Assert.Single(result);
        Assert.Equal(source.Id, ancestor.Artifact.Id);
    }

    [Fact]
    public async Task GetGeneratedFromAncestorsAsync_SkipsMissingAncestors()
    {
        var repository = new InMemoryEvidenceRepository();
        var service = new EvidenceLineageService(repository);

        var generated = new Artifact
        {
            Id = new ArtifactId("missing-generated"),
            Name = "Generated",
            ArtifactType = "intelligence-output"
        };

        await repository.AddArtifactAsync(generated);

        await repository.AddRelationshipAsync(
            new Relationship
            {
                SourceArtifactId = generated.Id,
                TargetArtifactId =
                    new ArtifactId("missing-source"),
                RelationshipType =
                    RelationshipTypes.GeneratedFrom
            });

        var result =
            await service.GetGeneratedFromAncestorsAsync(generated.Id);

        Assert.Empty(result);
    }

}
