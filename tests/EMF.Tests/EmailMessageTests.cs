using EMF.Discovery.Models.Email;

namespace EMF.Tests;

public sealed class EmailMessageTests
{
    [Fact]
    public void EmailMessage_PreservesCoreFields()
    {
        var message =
            new EmailMessage
            {
                MessageId = "<123@example.com>",
                From = "sender@example.com",
                To = ["recipient@example.com"],
                Cc = ["copy@example.com"],
                Bcc = ["blind@example.com"],
                Subject = "Evidence",
                DateUtc =
                    new DateTimeOffset(
                        2026,
                        8,
                        22,
                        18,
                        0,
                        0,
                        TimeSpan.Zero),
                TextBody = "Evidence text.",
                HtmlBody = "<p>Evidence text.</p>",
                SourceFormat = "EML"
            };

        Assert.Equal(
            "<123@example.com>",
            message.MessageId);

        Assert.Equal(
            "sender@example.com",
            message.From);

        Assert.Single(message.To);
        Assert.Single(message.Cc);
        Assert.Single(message.Bcc);

        Assert.Equal(
            "Evidence",
            message.Subject);

        Assert.Equal(
            "Evidence text.",
            message.TextBody);

        Assert.Equal(
            "EML",
            message.SourceFormat);
    }

    [Fact]
    public void EmailMessage_PreservesAttachments()
    {
        var message =
            new EmailMessage
            {
                SourceFormat = "EML",
                Attachments =
                [
                    new EmailAttachment
                    {
                        FileName = "medical-record.pdf",
                        ContentType = "application/pdf",
                        SizeBytes = 12345,
                        ContentId = "attachment-1",
                        IsInline = false
                    }
                ]
            };

        var attachment =
            Assert.Single(message.Attachments);

        Assert.Equal(
            "medical-record.pdf",
            attachment.FileName);

        Assert.Equal(
            "application/pdf",
            attachment.ContentType);

        Assert.Equal(
            12345,
            attachment.SizeBytes);

        Assert.False(attachment.IsInline);
    }
}
