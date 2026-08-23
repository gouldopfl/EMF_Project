using EMF.Discovery.Services;

namespace EMF.Tests;

public sealed class OfficePackageSignatureProviderTests
{
    [Theory]
    [InlineData(
        "evidence-sample.xlsx",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "XLSX")]
    [InlineData(
        "evidence-sample.odt",
        "application/vnd.oasis.opendocument.text",
        "ODT")]
    public async Task TryDetect_RecognizesKnownOfficePackages(
        string fileName,
        string expectedContentType,
        string expectedFormat)
    {
        var path =
            Path.Combine(
                AppContext.BaseDirectory,
                "TestData",
                fileName);

        var content =
            await File.ReadAllBytesAsync(path);

        var sample =
            content.AsSpan(
                0,
                Math.Min(
                    content.Length,
                    64 * 1024));

        var provider =
            new OfficePackageSignatureProvider();

        var detected =
            provider.TryDetect(
                sample,
                out var contentType,
                out var format);

        Assert.True(detected);
        Assert.Equal(expectedContentType, contentType);
        Assert.Equal(expectedFormat, format);
    }

    [Fact]
    public void TryDetect_RejectsGenericZip()
    {
        var provider =
            new OfficePackageSignatureProvider();

        var content =
            new byte[]
            {
                0x50, 0x4B, 0x03, 0x04,
                0x00, 0x00, 0x00, 0x00
            };

        Assert.False(
            provider.TryDetect(
                content,
                out _,
                out _));
    }
}
