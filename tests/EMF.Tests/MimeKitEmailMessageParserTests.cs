using System.Text;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class MimeKitEmailMessageParserTests
{
    [Fact]
    public async Task ParseAsync_MapsCoreMessageFields()
    {
        var eml =
            """
            From: sender@example.com
            To: recipient@example.com
            Subject: Evidence
            Message-ID: <123@example.com>
            Date: Sat, 22 Aug 2026 18:00:00 -0400
            MIME-Version: 1.0
            Content-Type: text/plain; charset=utf-8

            Veteran has chronic instability.
            """;

        var parser = new MimeKitEmailMessageParser();

        var message =
            await parser.ParseAsync(
                Encoding.UTF8.GetBytes(eml));

        Assert.Equal("123@example.com", message.MessageId);
        Assert.Equal("Evidence", message.Subject);
        Assert.Equal("EML", message.SourceFormat);
        Assert.Contains(
            "Veteran has chronic instability.",
            message.TextBody);
    }

    [Fact]
    public async Task ParseAsync_MapsAttachmentMetadata()
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

        var parser = new MimeKitEmailMessageParser();
        var message = await parser.ParseAsync(
            System.Text.Encoding.UTF8.GetBytes(eml));

        var attachment = Assert.Single(message.Attachments);

        Assert.Equal("record.pdf", attachment.FileName);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.False(attachment.IsInline);
    }
}
