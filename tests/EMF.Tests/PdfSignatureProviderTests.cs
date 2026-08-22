using EMF.Discovery.Services;

namespace EMF.Tests;

public sealed class PdfSignatureProviderTests
{
    [Fact]
    public void TryDetect_RecognizesPdf()
    {
        var provider = new PdfSignatureProvider();
        var content = "%PDF-1.7 test"u8.ToArray();

        var detected =
            provider.TryDetect(
                content,
                out var contentType,
                out var format);

        Assert.True(detected);
        Assert.Equal("application/pdf", contentType);
        Assert.Equal("PDF", format);
    }

    [Fact]
    public void TryDetect_RejectsShortContent()
    {
        var provider = new PdfSignatureProvider();

        Assert.False(
            provider.TryDetect(
                Array.Empty<byte>(),
                out _,
                out _));
    }

    [Fact]
    public void TryDetect_RejectsWrongSignature()
    {
        var provider = new PdfSignatureProvider();

        Assert.False(
            provider.TryDetect(
                "not a PDF"u8.ToArray(),
                out _,
                out _));
    }
}
