using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class RelationshipFactoryTests
{
    [Fact]
    public void RelationshipFactory_CreatesRelationship()
    {
        var factory = new RelationshipFactory();

        var result = factory.Create(
            new ArtifactId("source"),
            new ArtifactId("target"),
            RelationshipTypes.DerivedFrom);

        Assert.Equal(
            "source",
            result.Relationship.SourceArtifactId.Value);

        Assert.Equal(
            "target",
            result.Relationship.TargetArtifactId.Value);

        Assert.Equal(
            RelationshipTypes.DerivedFrom,
            result.Relationship.RelationshipType);
    }

    [Fact]
    public void RelationshipFactory_PreservesProperties()
    {
        var factory = new RelationshipFactory();

        var result = factory.Create(
            new ArtifactId("archive"),
            new ArtifactId("database"),
            RelationshipTypes.Contains,
            new Dictionary<string, object>
            {
                ["reason"] = "extracted file"
            });

        Assert.Equal(
            "extracted file",
            result.Relationship.Properties["reason"]);
    }
}
