using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class DefaultArtifactContentTypeResolverTests
{
    [Theory]
    [InlineData(".txt", "text/plain")]
    [InlineData(".rtf", "application/rtf")]
    [InlineData(".odt", "application/vnd.oasis.opendocument.text")]
    [InlineData(".ods", "application/vnd.oasis.opendocument.spreadsheet")]
    [InlineData(".odp", "application/vnd.oasis.opendocument.presentation")]
    [InlineData(".pdf", "application/pdf")]
    [InlineData(".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    [InlineData(".doc", "application/msword")]
    [InlineData(
        ".xlsx",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData(
        ".xls",
        "application/vnd.ms-excel")]
    [InlineData(".json", "application/json")]
    [InlineData(".csv", "text/csv")]
    [InlineData(".tsv", "text/csv")]
    [InlineData(".eml", "message/rfc822")]
    [InlineData(".xml", "application/xml")]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".jpeg", "image/jpeg")]
    [InlineData(".png", "image/png")]
    [InlineData(".tif", "image/tiff")]
    [InlineData(".tiff", "image/tiff")]
    [InlineData(".db", "application/x-sqlite3")]
    [InlineData(".sqlite", "application/x-sqlite3")]
    [InlineData(".sqlite3", "application/x-sqlite3")]
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
