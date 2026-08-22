using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class DefaultArtifactContentTypeResolverTests
{
    [Theory]
    [InlineData(".txt", "text/plain")]
    [InlineData(".pdf", "application/pdf")]
    [InlineData(
        ".xlsx",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData(".json", "application/json")]
    [InlineData(".csv", "text/csv")]
    [InlineData(".xml", "application/xml")]
    public void ResolveContentType_MapsKnownExtensions(
        string extension,
        string expected)
    {
        var artifact = new Artifact
        {
            Id = new ArtifactId("artifact-001"),
            Name = "evidence" + extension,
            ArtifactType = "file",
            Metadata = new Dictionary<string, object>
            {
                [ArtifactMetadataKeys.FileExtension] =
                    extension
            }
        };

        var resolver =
            new DefaultArtifactContentTypeResolver();

        Assert.Equal(
            expected,
            resolver.ResolveContentType(artifact));
    }

    [Fact]
    public void ResolveContentType_ReturnsNullForUnknownExtension()
    {
        var artifact = new Artifact
        {
            Id = new ArtifactId("artifact-002"),
            Name = "evidence.bin",
            ArtifactType = "file",
            Metadata = new Dictionary<string, object>
            {
                [ArtifactMetadataKeys.FileExtension] =
                    ".bin"
            }
        };

        var resolver =
            new DefaultArtifactContentTypeResolver();

        Assert.Null(
            resolver.ResolveContentType(artifact));
    }
}
