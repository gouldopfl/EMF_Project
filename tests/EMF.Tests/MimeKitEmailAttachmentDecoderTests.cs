using System.Text;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class MimeKitEmailAttachmentDecoderTests
{
    [Fact]
    public async Task DecodeAsync_DecodesAttachmentContent()
    {
        var eml =
            """
            From: sender@example.com
            To: recipient@example.com
            Subject: Evidence
            MIME-Version: 1.0
            Content-Type: multipart/mixed; boundary="abc"

            --abc
            Content-Type: text/plain

            Message body.
            --abc
            Content-Type: application/pdf; name="record.pdf"
            Content-Disposition: attachment; filename="record.pdf"
            Content-Transfer-Encoding: base64

            dGVzdA==
            --abc--
            """;

        var decoder = new MimeKitEmailAttachmentDecoder();

        var attachments =
            await decoder.DecodeAsync(
                Encoding.UTF8.GetBytes(eml));

        var attachment = Assert.Single(attachments);

        Assert.Equal("record.pdf", attachment.FileName);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.Equal("test", Encoding.UTF8.GetString(attachment.Content));
    }
    [Fact]
    public async Task DecodeAsync_RejectsTooManyAttachments()
    {
        var decoder =
            new MimeKitEmailAttachmentDecoder(
                maxAttachmentCount: 1,
                maxAttachmentBytes: 1024,
                maxTotalAttachmentBytes: 2048);

        var ex =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => decoder.DecodeAsync(
                    CreateEmail(
                        ("one.txt", "alpha"),
                        ("two.txt", "beta"))));

        Assert.Equal(
            "Email message exceeds the maximum allowed attachment count.",
            ex.Message);
    }

    [Fact]
    public async Task DecodeAsync_RejectsOversizedAttachment()
    {
        var decoder =
            new MimeKitEmailAttachmentDecoder(
                maxAttachmentCount: 10,
                maxAttachmentBytes: 3,
                maxTotalAttachmentBytes: 16);

        var ex =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => decoder.DecodeAsync(
                    CreateEmail(
                        ("record.txt", "test"))));

        Assert.Equal(
            "Email attachment exceeds the maximum allowed decoded size.",
            ex.Message);
    }

    [Fact]
    public async Task DecodeAsync_RejectsOversizedAttachmentTotal()
    {
        var decoder =
            new MimeKitEmailAttachmentDecoder(
                maxAttachmentCount: 10,
                maxAttachmentBytes: 16,
                maxTotalAttachmentBytes: 5);

        var ex =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => decoder.DecodeAsync(
                    CreateEmail(
                        ("one.txt", "abc"),
                        ("two.txt", "def"))));

        Assert.Equal(
            "Email attachments exceed the maximum allowed total decoded size.",
            ex.Message);
    }

    private static byte[] CreateEmail(
        params (string Name, string Content)[] attachments)
    {
        var builder = new StringBuilder();

        builder.AppendLine("From: sender@example.com");
        builder.AppendLine("To: recipient@example.com");
        builder.AppendLine("Subject: Evidence");
        builder.AppendLine("MIME-Version: 1.0");
        builder.AppendLine(
            "Content-Type: multipart/mixed; boundary=\"limit-boundary\"");
        builder.AppendLine();

        foreach (var attachment in attachments)
        {
            builder.AppendLine("--limit-boundary");
            builder.AppendLine(
                $"Content-Type: application/octet-stream; name=\"{attachment.Name}\"");
            builder.AppendLine(
                $"Content-Disposition: attachment; filename=\"{attachment.Name}\"");
            builder.AppendLine(
                "Content-Transfer-Encoding: base64");
            builder.AppendLine();
            builder.AppendLine(
                Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(
                        attachment.Content)));
        }

        builder.AppendLine("--limit-boundary--");

        return Encoding.UTF8.GetBytes(
            builder.ToString());
    }

}
