using EMF.Core.Models;
using EMF.Core.Models.Identities;

namespace EMF.Tests;

public sealed class RelationshipTests
{
    [Fact]
    public void Relationship_CreatesConnectionBetweenArtifacts()
    {
        var source = new ArtifactId("artifact-source");
        var target = new ArtifactId("artifact-target");

        var relationship = new Relationship
        {
            SourceArtifactId = source,
            TargetArtifactId = target,
            RelationshipType = RelationshipTypes.DerivedFrom
        };

        Assert.Equal(source, relationship.SourceArtifactId);
        Assert.Equal(target, relationship.TargetArtifactId);
        Assert.Equal(
            RelationshipTypes.DerivedFrom,
            relationship.RelationshipType);
    }

    [Fact]
    public void Relationship_SupportsProperties()
    {
        var relationship = new Relationship
        {
            SourceArtifactId = new ArtifactId("source"),
            TargetArtifactId = new ArtifactId("target"),
            RelationshipType = RelationshipTypes.Contains,
            Properties = new Dictionary<string, object>
            {
                ["reason"] = "archive extraction"
            }
        };

        Assert.Equal(
            "archive extraction",
            relationship.Properties["reason"]);
    }
}
