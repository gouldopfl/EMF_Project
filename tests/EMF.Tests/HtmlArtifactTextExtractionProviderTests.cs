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
