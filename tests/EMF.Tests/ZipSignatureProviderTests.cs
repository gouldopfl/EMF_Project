using EMF.Discovery.Services;

namespace EMF.Tests;

public sealed class ZipSignatureProviderTests
{
    [Fact]
    public void TryDetect_RecognizesZip()
    {
        var provider = new ZipSignatureProvider();

        var content =
            new byte[] { 0x50, 0x4B, 0x03, 0x04 };

        var detected =
            provider.TryDetect(
                content,
                out var contentType,
                out var format);

        Assert.True(detected);
        Assert.Equal("application/zip", contentType);
        Assert.Equal("ZIP", format);
    }

    [Fact]
    public void TryDetect_RejectsShortContent()
    {
        var provider = new ZipSignatureProvider();

        Assert.False(
            provider.TryDetect(
                Array.Empty<byte>(),
                out _,
                out _));
    }

    [Fact]
    public void TryDetect_RejectsWrongSignature()
    {
        var provider = new ZipSignatureProvider();

        Assert.False(
            provider.TryDetect(
                "not a ZIP"u8.ToArray(),
                out _,
                out _));
    }
}
