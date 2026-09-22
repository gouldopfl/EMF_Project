using EMF.ConsoleApplication;
using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.Tests;

public sealed class DocxArtifactPrintRenderingProviderTests
{
    private const string DocxContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    [Fact]
    public void CanRender_RecognizesDocxOnly()
    {
        var provider =
            new DocxArtifactPrintRenderingProvider(
                new StubContentStore(null),
                new RecordingConverter(),
                new StubPageRenderer());

        Assert.True(provider.CanRender(DocxContentType));
        Assert.True(provider.CanRender(DocxContentType.ToUpperInvariant()));
        Assert.False(provider.CanRender("application/pdf"));
        Assert.False(provider.CanRender("text/plain"));
    }

    [Fact]
    public async Task RenderAsync_MissingContentReturnsEmptyWithoutConversion()
    {
        var converter = new RecordingConverter();

        var provider =
            new DocxArtifactPrintRenderingProvider(
                new StubContentStore(null),
                converter,
                new StubPageRenderer());

        var result =
            await provider.RenderAsync(
                new ArtifactId("missing-docx"));

        Assert.Empty(result);
        Assert.Null(converter.Input);
    }

    [Fact]
    public async Task RenderAsync_PassesPreservedDocxToConverter()
    {
        var content =
            new byte[] { 0x50, 0x4B, 0x03, 0x04 };

        var converter =
            new RecordingConverter(
                throwAfterRecording: true);

        var provider =
            new DocxArtifactPrintRenderingProvider(
                new StubContentStore(content),
                converter,
                new StubPageRenderer());

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.RenderAsync(
                    new ArtifactId("docx-1")));

        Assert.Equal(
            "stop-after-conversion",
            exception.Message);

        Assert.Equal(
            content,
            converter.Input);
    }

    private sealed class RecordingConverter :
        IVeteransReviewerPackageDocumentConverter
    {
        private readonly bool _throwAfterRecording;

        public RecordingConverter(
            bool throwAfterRecording = false)
        {
            _throwAfterRecording =
                throwAfterRecording;
        }

        public byte[]? Input
        {
            get;
            private set;
        }

        public Task<byte[]> ConvertDocxToPdfAsync(
            ReadOnlyMemory<byte> docx,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Input = docx.ToArray();

            if (_throwAfterRecording)
            {
                throw new InvalidOperationException(
                    "stop-after-conversion");
            }

            return Task.FromResult(
                System.Text.Encoding.ASCII.GetBytes(
                    "%PDF-1.7\n%%EOF\n"));
        }
    }

    private sealed class StubPageRenderer :
        IPdfPageImageRenderer
    {
        public Task<byte[]> RenderPageAsync(
            ReadOnlyMemory<byte> pdf,
            int pageIndex,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubContentStore :
        IArtifactContentStore
    {
        private readonly byte[]? _content;

        public StubContentStore(
            byte[]? content)
        {
            _content = content;
        }

        public Task<byte[]?> ReadAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_content);
        }

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
}
