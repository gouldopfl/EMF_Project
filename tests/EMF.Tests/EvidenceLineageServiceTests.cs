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

    [Fact]
    public async Task GetGeneratedFromDescendantsAsync_ReturnsDirectGeneratedArtifact()
    {
        var repository = new InMemoryEvidenceRepository();
        var service = new EvidenceLineageService(repository);

        var source = new Artifact
        {
            Id = new ArtifactId("desc-source-001"),
            Name = "Source",
            ArtifactType = "file"
        };

        var generated = new Artifact
        {
            Id = new ArtifactId("desc-generated-001"),
            Name = "Generated",
            ArtifactType = "intelligence-output"
        };

        await repository.AddArtifactAsync(source);
        await repository.AddArtifactAsync(generated);

        await repository.AddRelationshipAsync(
            new Relationship
            {
                SourceArtifactId = generated.Id,
                TargetArtifactId = source.Id,
                RelationshipType = RelationshipTypes.GeneratedFrom
            });

        var result =
            await service.GetGeneratedFromDescendantsAsync(source.Id);

        var descendant = Assert.Single(result);

        Assert.Equal(
            generated.Id,
            descendant.Artifact.Id);

        Assert.Equal(
            generated.Id,
            descendant.Relationship.SourceArtifactId);

        Assert.Equal(
            source.Id,
            descendant.Relationship.TargetArtifactId);

        Assert.Equal(1, descendant.Depth);
    }

    [Fact]
    public async Task GetGeneratedFromDescendantsAsync_ReturnsRecursiveDescendants()
    {
        var repository = new InMemoryEvidenceRepository();
        var service = new EvidenceLineageService(repository);

        var source = new Artifact
        {
            Id = new ArtifactId("desc-source-002"),
            Name = "Source",
            ArtifactType = "file"
        };

        var firstGenerated = new Artifact
        {
            Id = new ArtifactId("desc-generated-002"),
            Name = "First generated",
            ArtifactType = "intelligence-output"
        };

        var secondGenerated = new Artifact
        {
            Id = new ArtifactId("desc-generated-003"),
            Name = "Second generated",
            ArtifactType = "intelligence-output"
        };

        await repository.AddArtifactAsync(source);
        await repository.AddArtifactAsync(firstGenerated);
        await repository.AddArtifactAsync(secondGenerated);

        await repository.AddRelationshipAsync(
            new Relationship
            {
                SourceArtifactId = firstGenerated.Id,
                TargetArtifactId = source.Id,
                RelationshipType = RelationshipTypes.GeneratedFrom
            });

        await repository.AddRelationshipAsync(
            new Relationship
            {
                SourceArtifactId = secondGenerated.Id,
                TargetArtifactId = firstGenerated.Id,
                RelationshipType = RelationshipTypes.GeneratedFrom
            });

        var result =
            await service.GetGeneratedFromDescendantsAsync(source.Id);

        Assert.Equal(2, result.Count);

        var firstNode =
            Assert.Single(
                result.Where(
                    node =>
                        node.Artifact.Id == firstGenerated.Id));

        var secondNode =
            Assert.Single(
                result.Where(
                    node =>
                        node.Artifact.Id == secondGenerated.Id));

        Assert.Equal(1, firstNode.Depth);
        Assert.Equal(2, secondNode.Depth);
    }

    [Fact]
    public async Task GetGeneratedFromDescendantsAsync_IgnoresCycles()
    {
        var repository = new InMemoryEvidenceRepository();
        var service = new EvidenceLineageService(repository);

        var first = new Artifact
        {
            Id = new ArtifactId("desc-cycle-001"),
            Name = "First",
            ArtifactType = "intelligence-output"
        };

        var second = new Artifact
        {
            Id = new ArtifactId("desc-cycle-002"),
            Name = "Second",
            ArtifactType = "intelligence-output"
        };

        await repository.AddArtifactAsync(first);
        await repository.AddArtifactAsync(second);

        await repository.AddRelationshipAsync(
            new Relationship
            {
                SourceArtifactId = second.Id,
                TargetArtifactId = first.Id,
                RelationshipType = RelationshipTypes.GeneratedFrom
            });

        await repository.AddRelationshipAsync(
            new Relationship
            {
                SourceArtifactId = first.Id,
                TargetArtifactId = second.Id,
                RelationshipType = RelationshipTypes.GeneratedFrom
            });

        var result =
            await service.GetGeneratedFromDescendantsAsync(first.Id);

        var descendant = Assert.Single(result);

        Assert.Equal(
            second.Id,
            descendant.Artifact.Id);
    }

    [Fact]
    public async Task GetGeneratedFromPathAsync_ReturnsDirectPath()
    {
        var repository = new InMemoryEvidenceRepository();
        var service = new EvidenceLineageService(repository);

        var generated = new Artifact
        {
            Id = new ArtifactId("path-generated-001"),
            Name = "Generated",
            ArtifactType = "intelligence-output"
        };

        var source = new Artifact
        {
            Id = new ArtifactId("path-source-001"),
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
            await service.GetGeneratedFromPathAsync(
                generated.Id,
                source.Id);

        Assert.NotNull(result);
        Assert.Equal(generated.Id, result!.StartArtifact.Id);
        Assert.Equal(source.Id, result.EndArtifact.Id);

        var node = Assert.Single(result.Nodes);
        Assert.Equal(source.Id, node.Artifact.Id);
        Assert.Equal(1, node.Depth);
    }

    [Fact]
    public async Task GetGeneratedFromPathAsync_ReturnsRecursivePath()
    {
        var repository = new InMemoryEvidenceRepository();
        var service = new EvidenceLineageService(repository);

        var generated = new Artifact
        {
            Id = new ArtifactId("path-generated-002"),
            Name = "Generated",
            ArtifactType = "intelligence-output"
        };

        var intermediate = new Artifact
        {
            Id = new ArtifactId("path-intermediate-002"),
            Name = "Intermediate",
            ArtifactType = "intelligence-output"
        };

        var source = new Artifact
        {
            Id = new ArtifactId("path-source-002"),
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
            await service.GetGeneratedFromPathAsync(
                generated.Id,
                source.Id);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Nodes.Count);

        Assert.Equal(
            intermediate.Id,
            result.Nodes[0].Artifact.Id);

        Assert.Equal(
            source.Id,
            result.Nodes[1].Artifact.Id);

        Assert.Equal(1, result.Nodes[0].Depth);
        Assert.Equal(2, result.Nodes[1].Depth);
    }

    [Fact]
    public async Task GetGeneratedFromPathAsync_ReturnsNullWhenNoPathExists()
    {
        var repository = new InMemoryEvidenceRepository();
        var service = new EvidenceLineageService(repository);

        var first = new Artifact
        {
            Id = new ArtifactId("path-unconnected-001"),
            Name = "First",
            ArtifactType = "file"
        };

        var second = new Artifact
        {
            Id = new ArtifactId("path-unconnected-002"),
            Name = "Second",
            ArtifactType = "file"
        };

        await repository.AddArtifactAsync(first);
        await repository.AddArtifactAsync(second);

        var result =
            await service.GetGeneratedFromPathAsync(
                first.Id,
                second.Id);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetGeneratedFromPathAsync_HandlesCycles()
    {
        var repository = new InMemoryEvidenceRepository();
        var service = new EvidenceLineageService(repository);

        var first = new Artifact
        {
            Id = new ArtifactId("path-cycle-001"),
            Name = "First",
            ArtifactType = "intelligence-output"
        };

        var second = new Artifact
        {
            Id = new ArtifactId("path-cycle-002"),
            Name = "Second",
            ArtifactType = "intelligence-output"
        };

        var target = new Artifact
        {
            Id = new ArtifactId("path-cycle-target"),
            Name = "Target",
            ArtifactType = "file"
        };

        await repository.AddArtifactAsync(first);
        await repository.AddArtifactAsync(second);
        await repository.AddArtifactAsync(target);

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

        await repository.AddRelationshipAsync(
            new Relationship
            {
                SourceArtifactId = second.Id,
                TargetArtifactId = target.Id,
                RelationshipType = RelationshipTypes.GeneratedFrom
            });

        var result =
            await service.GetGeneratedFromPathAsync(
                first.Id,
                target.Id);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Nodes.Count);
        Assert.Equal(target.Id, result.EndArtifact.Id);
    }

    [Fact]
    public async Task GetGeneratedFromPathAsync_ReturnsShortestPath()
    {
        var repository = new InMemoryEvidenceRepository();
        var service = new EvidenceLineageService(repository);

        var start = new Artifact
        {
            Id = new ArtifactId("path-shortest-start"),
            Name = "Start",
            ArtifactType = "intelligence-output"
        };

        var shortMiddle = new Artifact
        {
            Id = new ArtifactId("path-shortest-middle"),
            Name = "Short middle",
            ArtifactType = "intelligence-output"
        };

        var longOne = new Artifact
        {
            Id = new ArtifactId("path-long-1"),
            Name = "Long one",
            ArtifactType = "intelligence-output"
        };

        var longTwo = new Artifact
        {
            Id = new ArtifactId("path-long-2"),
            Name = "Long two",
            ArtifactType = "intelligence-output"
        };

        var target = new Artifact
        {
            Id = new ArtifactId("path-shortest-target"),
            Name = "Target",
            ArtifactType = "file"
        };

        foreach (var artifact in new[]
        {
            start,
            shortMiddle,
            longOne,
            longTwo,
            target
        })
        {
            await repository.AddArtifactAsync(artifact);
        }

        await repository.AddRelationshipAsync(
            new Relationship
            {
                SourceArtifactId = start.Id,
                TargetArtifactId = shortMiddle.Id,
                RelationshipType = RelationshipTypes.GeneratedFrom
            });

        await repository.AddRelationshipAsync(
            new Relationship
            {
                SourceArtifactId = shortMiddle.Id,
                TargetArtifactId = target.Id,
                RelationshipType = RelationshipTypes.GeneratedFrom
            });

        await repository.AddRelationshipAsync(
            new Relationship
            {
                SourceArtifactId = start.Id,
                TargetArtifactId = longOne.Id,
                RelationshipType = RelationshipTypes.GeneratedFrom
            });

        await repository.AddRelationshipAsync(
            new Relationship
            {
                SourceArtifactId = longOne.Id,
                TargetArtifactId = longTwo.Id,
                RelationshipType = RelationshipTypes.GeneratedFrom
            });

        await repository.AddRelationshipAsync(
            new Relationship
            {
                SourceArtifactId = longTwo.Id,
                TargetArtifactId = target.Id,
                RelationshipType = RelationshipTypes.GeneratedFrom
            });

        var result =
            await service.GetGeneratedFromPathAsync(
                start.Id,
                target.Id);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Nodes.Count);
        Assert.Equal(shortMiddle.Id, result.Nodes[0].Artifact.Id);
        Assert.Equal(target.Id, result.Nodes[1].Artifact.Id);
    }

    [Fact]
    public async Task GetGeneratedFromPathAsync_ReturnsNullWhenStartArtifactMissing()
    {
        var repository = new InMemoryEvidenceRepository();
        var service = new EvidenceLineageService(repository);

        var end = new Artifact
        {
            Id = new ArtifactId("path-end-existing"),
            Name = "End",
            ArtifactType = "file"
        };

        await repository.AddArtifactAsync(end);

        var result =
            await service.GetGeneratedFromPathAsync(
                new ArtifactId("path-start-missing"),
                end.Id);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetGeneratedFromPathAsync_ReturnsNullWhenEndArtifactMissing()
    {
        var repository = new InMemoryEvidenceRepository();
        var service = new EvidenceLineageService(repository);

        var start = new Artifact
        {
            Id = new ArtifactId("path-start-existing"),
            Name = "Start",
            ArtifactType = "intelligence-output"
        };

        await repository.AddArtifactAsync(start);

        var result =
            await service.GetGeneratedFromPathAsync(
                start.Id,
                new ArtifactId("path-end-missing"));

        Assert.Null(result);
    }

    [Fact]
    public async Task GetGeneratedFromPathAsync_ReturnsZeroHopPathWhenEndpointsMatch()
    {
        var repository = new InMemoryEvidenceRepository();
        var service = new EvidenceLineageService(repository);

        var artifact = new Artifact
        {
            Id = new ArtifactId("path-same-001"),
            Name = "Same artifact",
            ArtifactType = "file"
        };

        await repository.AddArtifactAsync(artifact);

        var result =
            await service.GetGeneratedFromPathAsync(
                artifact.Id,
                artifact.Id);

        Assert.NotNull(result);
        Assert.Equal(artifact.Id, result!.StartArtifact.Id);
        Assert.Equal(artifact.Id, result.EndArtifact.Id);
        Assert.Empty(result.Nodes);
    }

    [Fact]
    public async Task IsGeneratedFromAsync_ReturnsTrueWhenPathExists()
    {
        var repository = new InMemoryEvidenceRepository();
        var service = new EvidenceLineageService(repository);

        var generated = new Artifact
        {
            Id = new ArtifactId("is-generated-001"),
            Name = "Generated",
            ArtifactType = "intelligence-output"
        };

        var source = new Artifact
        {
            Id = new ArtifactId("is-source-001"),
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
            await service.IsGeneratedFromAsync(
                generated.Id,
                source.Id);

        Assert.True(result);
    }

    [Fact]
    public async Task IsGeneratedFromAsync_ReturnsFalseWhenNoPathExists()
    {
        var repository = new InMemoryEvidenceRepository();
        var service = new EvidenceLineageService(repository);

        var first = new Artifact
        {
            Id = new ArtifactId("is-first-001"),
            Name = "First",
            ArtifactType = "file"
        };

        var second = new Artifact
        {
            Id = new ArtifactId("is-second-001"),
            Name = "Second",
            ArtifactType = "file"
        };

        await repository.AddArtifactAsync(first);
        await repository.AddArtifactAsync(second);

        var result =
            await service.IsGeneratedFromAsync(
                first.Id,
                second.Id);

        Assert.False(result);
    }

}
