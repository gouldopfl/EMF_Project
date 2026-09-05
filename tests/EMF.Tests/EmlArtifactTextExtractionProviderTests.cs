using System.Text;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class EmlArtifactTextExtractionProviderTests
{
    [Fact]
    public async Task ExtractTextAsync_ReturnsMessageBody()
    {
        var content =
            Encoding.UTF8.GetBytes(
                "From: sender@example.com\r\n" +
                "To: recipient@example.com\r\n" +
                "Subject: Evidence\r\n" +
                "\r\n" +
                "Veteran has chronic instability.");

        var provider =
            new EmlArtifactTextExtractionProvider(
                new StubContentStore(content));

        var text =
            await provider.ExtractTextAsync(
                new ArtifactId("eml-001"));

        Assert.Equal(
            "Veteran has chronic instability.",
            text);
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsOversizedInput()
    {
        var provider =
            new EmlArtifactTextExtractionProvider(
                new StubContentStore(CreateMessage()),
                maxInputBytes: 1);

        var exception =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => provider.ExtractTextAsync(
                    new ArtifactId("eml-input-limit")));

        Assert.Equal(
            "EML input exceeds the maximum allowed size.",
            exception.Message);
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsOversizedBody()
    {
        var provider =
            new EmlArtifactTextExtractionProvider(
                new StubContentStore(CreateMessage()),
                maxExtractedTextChars: 1);

        var exception =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => provider.ExtractTextAsync(
                    new ArtifactId("eml-body-limit")));

        Assert.Equal(
            "EML message body exceeds " +
            "the maximum allowed size.",
            exception.Message);
    }

    [Fact]
    public async Task ExtractTextAsync_ReturnsNullWhenContentMissing()
    {
        var provider =
            new EmlArtifactTextExtractionProvider(
                new StubContentStore(null));

        var result =
            await provider.ExtractTextAsync(
                new ArtifactId("eml-missing"));

        Assert.Null(result);
    }

    [Fact]
    public async Task ExtractTextAsync_HonorsCancellation()
    {
        using var cancellation =
            new CancellationTokenSource();

        cancellation.Cancel();

        var provider =
            new EmlArtifactTextExtractionProvider(
                new StubContentStore(
                    Array.Empty<byte>()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.ExtractTextAsync(
                new ArtifactId("eml-cancelled"),
                cancellation.Token));
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsMissingSeparator()
    {
        var provider =
            new EmlArtifactTextExtractionProvider(
                new StubContentStore(
                    "From: sender@example.com"u8.ToArray()));

        await Assert.ThrowsAsync<FormatException>(
            () => provider.ExtractTextAsync(
                new ArtifactId("eml-invalid")));
    }

    [Fact]
    public void CanExtract_IsCaseInsensitive()
    {
        var provider =
            new EmlArtifactTextExtractionProvider(
                new StubContentStore(null));

        Assert.True(
            provider.CanExtract("MESSAGE/RFC822"));

        Assert.False(
            provider.CanExtract("text/plain"));
    }

    private static byte[] CreateMessage() =>
        Encoding.UTF8.GetBytes(
            "From: sender@example.com\r\n" +
            "Subject: Evidence\r\n" +
            "\r\n" +
            "Veteran has chronic instability.");

    private sealed class StubContentStore :
        IArtifactContentStore
    {
        private readonly byte[]? _content;

        public StubContentStore(byte[]? content)
        {
            _content = content;
        }

        public Task<byte[]?> ReadAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<byte[]?>(_content);
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
