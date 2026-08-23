using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Persistence.Repositories;

namespace EMF.Tests;

public sealed class ArtifactMetadataMergePersistenceTests
{
    [Fact]
    public async Task MergeArtifactMetadataAsync_MergesAndOverwrites()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository = new SqliteEvidenceRepository(path);
            await repository.InitializeAsync();

            var id = new ArtifactId("merge-001");

            await repository.AddArtifactAsync(
                new Artifact
                {
                    Id = id,
                    Name = "evidence.pdf",
                    ArtifactType = "file",
                    Metadata = new Dictionary<string, object>
                    {
                        ["existing"] = "preserved",
                        ["contentType"] = "old/type"
                    }
                });

            await repository.MergeArtifactMetadataAsync(
                id,
                new Dictionary<string, object>
                {
                    ["contentType"] = "application/pdf",
                    ["detectedFormat"] = "PDF"
                });

            var artifact = await repository.GetArtifactAsync(id);

            Assert.NotNull(artifact);
            Assert.Equal(
                "preserved",
                artifact.Metadata["existing"].ToString());
            Assert.Equal(
                "application/pdf",
                artifact.Metadata["contentType"].ToString());
            Assert.Equal(
                "PDF",
                artifact.Metadata["detectedFormat"].ToString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task MergeArtifactMetadataAsync_ThrowsWhenArtifactMissing()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository = new SqliteEvidenceRepository(path);
            await repository.InitializeAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => repository.MergeArtifactMetadataAsync(
                    new ArtifactId("missing"),
                    new Dictionary<string, object>
                    {
                        ["contentType"] = "application/pdf"
                    }));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
