using System.Text;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class HtmlArtifactPrintRenderingProviderTests
{
    [Fact]
    public void CanRender_RecognizesHtml()
    {
        var provider = CreateProvider("<p>evidence</p>");

        Assert.True(provider.CanRender("text/html"));
        Assert.True(provider.CanRender("TEXT/HTML"));
        Assert.False(provider.CanRender("text/plain"));
    }

    [Fact]
    public async Task RenderAsync_ReturnsVisibleText()
    {
        const string html =
            "<html><body>" +
            "<h1>Medical Literature</h1>" +
            "<p>PTSD &amp; OSA evidence.</p>" +
            "<script>ignoreThis()</script>" +
            "</body></html>";

        var provider = CreateProvider(html);

        var pages = await provider.RenderAsync(
            new ArtifactId("html-print"));

        var page = Assert.Single(pages);

        Assert.Equal(1, page.PageNumber);
        Assert.Equal("text/plain", page.ContentType);

        var text = Encoding.UTF8.GetString(
            page.Content.ToArray());

        Assert.Contains("Medical Literature", text);
        Assert.Contains("PTSD & OSA evidence.", text);
        Assert.DoesNotContain("ignoreThis", text);
        Assert.DoesNotContain("<p>", text);
    }

    [Fact]
    public async Task RenderAsync_ReturnsEmptyWhenMissing()
    {
        var extractor =
            new HtmlArtifactTextExtractionProvider(
                new StubContentStore(null));

        var provider =
            new HtmlArtifactPrintRenderingProvider(extractor);

        var pages = await provider.RenderAsync(
            new ArtifactId("html-missing"));

        Assert.Empty(pages);
    }

    private static HtmlArtifactPrintRenderingProvider
        CreateProvider(string html) =>
        new(
            new HtmlArtifactTextExtractionProvider(
                new StubContentStore(
                    Encoding.UTF8.GetBytes(html))));

    private sealed class StubContentStore(byte[]? content) :
        IArtifactContentStore
    {
        public Task<byte[]?> ReadAsync(
            ArtifactId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(content);

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
