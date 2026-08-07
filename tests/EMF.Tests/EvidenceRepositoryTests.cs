using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class EvidenceRepositoryTests
{
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
}
