using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class OutlookMessageDecoderTests
{
    [Fact]
    public async Task DecodeAsync_ReadsOutlookMessage()
    {
        var path =
            Path.Combine(
                AppContext.BaseDirectory,
                "TestData",
                "TxtSampleEmail.msg");

        var content =
            await File.ReadAllBytesAsync(path);

        var message =
            await new OutlookMessageDecoder()
                .DecodeAsync(content);

        Assert.False(
            string.IsNullOrWhiteSpace(message.Subject));

        Assert.False(
            string.IsNullOrWhiteSpace(message.BodyText));
    }

    [Fact]
    public async Task DecodeAsync_ReturnsAttachments()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "TxtSampleEmailWithAttachment.msg");

        var content =
            await File.ReadAllBytesAsync(path);

        var message =
            await new OutlookMessageDecoder()
                .DecodeAsync(content);

        Assert.NotEmpty(message.Attachments);

        var attachment = message.Attachments[0];

        Assert.False(
            string.IsNullOrWhiteSpace(
                attachment.FileName));

        Assert.NotEmpty(attachment.Content);
    }

    [Fact]
    public async Task DecodeAsync_HonorsCancellation()
    {
        using var cancellation =
            new CancellationTokenSource();

        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => new OutlookMessageDecoder().DecodeAsync(
                Array.Empty<byte>(),
                cancellation.Token));
    }
}
