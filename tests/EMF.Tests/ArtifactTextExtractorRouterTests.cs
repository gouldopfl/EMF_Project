using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class ArtifactTextExtractorRouterTests
{
    [Fact]
    public async Task ExtractTextAsync_ReturnsNullWhenArtifactMissing()
    {
        var router =
            new ArtifactTextExtractorRouter(
                new InMemoryEvidenceRepository(),
                new DefaultArtifactContentTypeResolver(),
                []);

        var result =
            await router.ExtractTextAsync(
                new ArtifactId("missing"));

        Assert.Null(result);
    }

    [Fact]
    public async Task ExtractTextAsync_ReturnsNullWhenContentTypeUnknown()
    {
        var repository =
            new InMemoryEvidenceRepository();

        var artifact =
            CreateArtifact(
                "artifact-001",
                ".bin");

        await repository.AddArtifactAsync(artifact);

        var router =
            new ArtifactTextExtractorRouter(
                repository,
                new DefaultArtifactContentTypeResolver(),
                []);

        Assert.Null(
            await router.ExtractTextAsync(artifact.Id));
    }

    [Fact]
    public async Task ExtractTextAsync_UsesMatchingProvider()
    {
        var repository =
            new InMemoryEvidenceRepository();

        var artifact =
            CreateArtifact(
                "artifact-002",
                ".txt");

        await repository.AddArtifactAsync(artifact);

        var provider =
            new StubProvider(
                "text/plain",
                "recognized text");

        var router =
            new ArtifactTextExtractorRouter(
                repository,
                new DefaultArtifactContentTypeResolver(),
                [provider]);

        Assert.Equal(
            "recognized text",
            await router.ExtractTextAsync(artifact.Id));
    }

    [Fact]
    public async Task ExtractTextAsync_RoutesPdfToPdfProvider()
    {
        var repository =
            new InMemoryEvidenceRepository();

        var artifact =
            CreateArtifact(
                "artifact-pdf-001",
                ".pdf");

        await repository.AddArtifactAsync(artifact);

        var pdfPath =
            Path.Combine(
                AppContext.BaseDirectory,
                "TestData",
                "evidence-sample.pdf");

        var content =
            await File.ReadAllBytesAsync(pdfPath);

        var router =
            new ArtifactTextExtractorRouter(
                repository,
                new DefaultArtifactContentTypeResolver(),
                [
                    new PdfArtifactTextExtractionProvider(
                        new StubContentStore(content))
                ]);

        var text =
            await router.ExtractTextAsync(artifact.Id);

        Assert.NotNull(text);
        Assert.Contains(
            "Veteran has chronic instability.",
            text);
        Assert.Contains(
            "MRI confirms lumbar degenerative changes.",
            text);
    }

    [Fact]
    public async Task ExtractTextAsync_ThrowsWhenKnownTypeUnsupported()
    {
        var repository =
            new InMemoryEvidenceRepository();

        var artifact =
            CreateArtifact(
                "artifact-003",
                ".pdf");

        await repository.AddArtifactAsync(artifact);

        var router =
            new ArtifactTextExtractorRouter(
                repository,
                new DefaultArtifactContentTypeResolver(),
                []);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => router.ExtractTextAsync(artifact.Id));
    }

    private static Artifact CreateArtifact(
        string id,
        string extension) =>
        new()
        {
            Id = new ArtifactId(id),
            Name = "evidence" + extension,
            ArtifactType = "file",
            Metadata = new Dictionary<string, object>
            {
                [ArtifactMetadataKeys.FileExtension] =
                    extension
            }
        };

    private sealed class StubContentStore :
        EMF.Core.Contracts.Storage.IArtifactContentStore
    {
        private readonly byte[] _content;

        public StubContentStore(byte[] content)
        {
            _content = content;
        }

        public Task<byte[]?> ReadAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<byte[]?>(_content);

        public Task WriteAsync(
            ArtifactId artifactId,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubProvider :
        IArtifactTextExtractionProvider
    {
        private readonly string _contentType;
        private readonly string _text;

        public StubProvider(
            string contentType,
            string text)
        {
            _contentType = contentType;
            _text = text;
        }

        public bool CanExtract(string contentType) =>
            string.Equals(
                contentType,
                _contentType,
                StringComparison.OrdinalIgnoreCase);

        public Task<string?> ExtractTextAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(_text);
    }
}
