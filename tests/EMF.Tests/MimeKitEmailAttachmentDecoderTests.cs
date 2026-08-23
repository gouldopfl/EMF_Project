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
}
