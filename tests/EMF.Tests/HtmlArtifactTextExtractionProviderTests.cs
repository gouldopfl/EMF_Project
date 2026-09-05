using System.Text;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class HtmlArtifactTextExtractionProviderTests
{
    [Fact]
    public async Task ExtractTextAsync_ReadsVisibleHtmlText()
    {
        const string html =
            "<html><body>" +
            "<h1>EMF Evidence</h1>" +
            "<p>MRI confirms lumbar changes &amp; pain.</p>" +
            "<script>ignoreThis()</script>" +
            "<style>.x{display:none}</style>" +
            "</body></html>";

        var provider =
            new HtmlArtifactTextExtractionProvider(
                new StubContentStore(
                    Encoding.UTF8.GetBytes(html)));

        var text =
            await provider.ExtractTextAsync(
                new ArtifactId("html-001"));

        Assert.Contains("EMF Evidence", text);
        Assert.Contains(
            "MRI confirms lumbar changes & pain.",
            text);
        Assert.DoesNotContain("ignoreThis", text);
        Assert.DoesNotContain("display:none", text);
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsOversizedInput()
    {
        var provider =
            new HtmlArtifactTextExtractionProvider(
                new StubContentStore(
                    Encoding.UTF8.GetBytes("<p>evidence</p>")),
                maxInputBytes: 1);

        var exception =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => provider.ExtractTextAsync(
                    new ArtifactId("html-input-limit")));

        Assert.Equal(
            "HTML input exceeds the maximum allowed size.",
            exception.Message);
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsTooManyTokens()
    {
        var provider =
            new HtmlArtifactTextExtractionProvider(
                new StubContentStore(
                    Encoding.UTF8.GetBytes(
                        "<p>one</p><p>two</p>")),
                maxTokenCount: 1);

        var exception =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => provider.ExtractTextAsync(
                    new ArtifactId("html-token-limit")));

        Assert.Equal(
            "HTML input exceeds the maximum " +
            "allowed token count.",
            exception.Message);
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsOversizedText()
    {
        var provider =
            new HtmlArtifactTextExtractionProvider(
                new StubContentStore(
                    Encoding.UTF8.GetBytes("<p>evidence</p>")),
                maxExtractedTextChars: 1);

        var exception =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => provider.ExtractTextAsync(
                    new ArtifactId("html-text-limit")));

        Assert.Equal(
            "HTML extracted text exceeds " +
            "the maximum allowed size.",
            exception.Message);
    }

    private sealed class StubContentStore(byte[] content) :
        IArtifactContentStore
    {
        public Task<byte[]?> ReadAsync(
            ArtifactId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<byte[]?>(content);

        public Task WriteAsync(
            ArtifactId id,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            ArtifactId id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
