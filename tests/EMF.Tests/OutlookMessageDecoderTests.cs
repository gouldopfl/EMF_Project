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
    [Fact]
    public void Constructor_RejectsInvalidAttachmentCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new OutlookMessageDecoder(
                maxAttachmentCount: 0));
    }

    [Fact]
    public async Task DecodeAsync_RejectsOversizedAttachment()
    {
        var path =
            Path.Combine(
                AppContext.BaseDirectory,
                "TestData",
                "TxtSampleEmailWithAttachment.msg");

        var content =
            await File.ReadAllBytesAsync(path);

        var decoder =
            new OutlookMessageDecoder(
                maxAttachmentCount: 10,
                maxAttachmentBytes: 1,
                maxTotalAttachmentBytes: 1024);

        var ex =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => decoder.DecodeAsync(content));

        Assert.Equal(
            "Outlook attachment exceeds the maximum allowed decoded size.",
            ex.Message);
    }

    [Fact]
    public async Task DecodeAsync_RejectsOversizedAttachmentTotal()
    {
        var path =
            Path.Combine(
                AppContext.BaseDirectory,
                "TestData",
                "TxtSampleEmailWithAttachment.msg");

        var content =
            await File.ReadAllBytesAsync(path);

        var decoder =
            new OutlookMessageDecoder(
                maxAttachmentCount: 10,
                maxAttachmentBytes:
                    OutlookMessageDecoder.DefaultMaxAttachmentBytes,
                maxTotalAttachmentBytes: 1);

        var ex =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => decoder.DecodeAsync(content));

        Assert.Equal(
            "Outlook attachments exceed the maximum allowed total decoded size.",
            ex.Message);
    }

}
