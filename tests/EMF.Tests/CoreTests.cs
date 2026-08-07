using EMF.Core.Models;
using EMF.Core.Models.Identities;

public sealed class CoreTests
{
    [Fact]
    public void Artifact_PreservesIdentityAndMetadata()
    {
        var artifact = new Artifact
        {
            Id = new ArtifactId("artifact-001"),
            Name = "oscar.db",
            ArtifactType = "database",
            Metadata = new Dictionary<string, object>
            {
                ["sourcePath"] = "/opt/emf-lab/datasets/LD-VET-001/extracted/oscar.db"
            }
        };

        Assert.Equal("artifact-001", artifact.Id.Value);
        Assert.Equal("oscar.db", artifact.Name);
        Assert.Equal("database", artifact.ArtifactType);
        Assert.Equal(
            "/opt/emf-lab/datasets/LD-VET-001/extracted/oscar.db",
            artifact.Metadata["sourcePath"]);
    }
}

public sealed class ProvenanceTests
{
    [Fact]
    public void Provenance_PreservesSourceAndRecorder()
    {
        var provenance = new Provenance
        {
            ArtifactId = new ArtifactId("artifact-001"),
            Source = "/opt/emf-lab/datasets/LD-VET-001/extracted/oscar.db",
            RecordedBy = "EMF.Discovery",
            Properties = new Dictionary<string, object>
            {
                ["sourceType"] = "file"
            }
        };

        Assert.Equal("artifact-001", provenance.ArtifactId.Value);
        Assert.Equal(
            "/opt/emf-lab/datasets/LD-VET-001/extracted/oscar.db",
            provenance.Source);
        Assert.Equal("EMF.Discovery", provenance.RecordedBy);
        Assert.Equal("file", provenance.Properties["sourceType"]);
    }
}

public sealed class RelationshipTests
{
    [Fact]
    public void Relationship_PreservesArtifactLink()
    {
        var relationship = new Relationship
        {
            SourceArtifactId = new ArtifactId("artifact-source"),
            TargetArtifactId = new ArtifactId("artifact-target"),
            RelationshipType = "derived-from",
            Properties = new Dictionary<string, object>
            {
                ["reason"] = "inventory"
            }
        };

        Assert.Equal(
            "artifact-source",
            relationship.SourceArtifactId.Value);

        Assert.Equal(
            "artifact-target",
            relationship.TargetArtifactId.Value);

        Assert.Equal(
            "derived-from",
            relationship.RelationshipType);

        Assert.Equal(
            "inventory",
            relationship.Properties["reason"]);
    }
}
