using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class ContainerAncestryGuardTests
{
    [Fact]
    public async Task ValidateAsync_AllowsContainerWithinDepth()
    {
        var repository = new InMemoryEvidenceRepository();

        var parent = CreateContainer("parent", "parent.zip");
        var child = CreateContainer("child", "child.eml");

        await repository.AddArtifactAsync(parent);
        await repository.AddArtifactAsync(child);

        await repository.AddRelationshipAsync(
            new Relationship
            {
                SourceArtifactId = child.Id,
                TargetArtifactId = parent.Id,
                RelationshipType = RelationshipTypes.DerivedFrom
            });

        var guard =
            new ContainerAncestryGuard(
                repository,
                maxContainerDepth: 2);

        await guard.ValidateAsync(child);
    }

    [Fact]
    public async Task ValidateAsync_RejectsExcessiveContainerDepth()
    {
        var repository = new InMemoryEvidenceRepository();

        var root = CreateContainer("root", "root.zip");
        var parent = CreateContainer("parent", "parent.msg");
        var child = CreateContainer("child", "child.eml");

        await repository.AddArtifactAsync(root);
        await repository.AddArtifactAsync(parent);
        await repository.AddArtifactAsync(child);

        await repository.AddRelationshipAsync(
            new Relationship
            {
                SourceArtifactId = child.Id,
                TargetArtifactId = parent.Id,
                RelationshipType = RelationshipTypes.DerivedFrom
            });

        await repository.AddRelationshipAsync(
            new Relationship
            {
                SourceArtifactId = parent.Id,
                TargetArtifactId = root.Id,
                RelationshipType = RelationshipTypes.DerivedFrom
            });

        var guard =
            new ContainerAncestryGuard(
                repository,
                maxContainerDepth: 2);

        var ex =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => guard.ValidateAsync(child));

        Assert.Equal(
            "Container nesting exceeds the maximum allowed depth.",
            ex.Message);
    }

    [Fact]
    public async Task ValidateAsync_RejectsMissingAncestor()
    {
        var repository = new InMemoryEvidenceRepository();

        var child = CreateContainer("child", "child.zip");

        await repository.AddArtifactAsync(child);

        await repository.AddRelationshipAsync(
            new Relationship
            {
                SourceArtifactId = child.Id,
                TargetArtifactId = new ArtifactId("missing"),
                RelationshipType = RelationshipTypes.DerivedFrom
            });

        var guard = new ContainerAncestryGuard(repository);

        var ex =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => guard.ValidateAsync(child));

        Assert.Equal(
            "Container ancestry references a missing artifact.",
            ex.Message);
    }

    private static Artifact CreateContainer(
        string id,
        string name) =>
        new()
        {
            Id = new ArtifactId(id),
            Name = name,
            ArtifactType = "file",
            Metadata = new Dictionary<string, object>
            {
                [ArtifactMetadataKeys.FileExtension] =
                    Path.GetExtension(name)
            }
        };
}
