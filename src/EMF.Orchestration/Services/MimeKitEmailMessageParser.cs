using EMF.Discovery.Models.Email;
using EMF.Orchestration.Contracts;
using MimeKit;

namespace EMF.Orchestration.Services;

public sealed class MimeKitEmailMessageParser :
    IEmailMessageParser
{
    public async Task<EmailMessage> ParseAsync(
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        await using var stream =
            new MemoryStream(content.ToArray(), writable: false);

        var message =
            await MimeMessage.LoadAsync(
                stream,
                cancellationToken);

        return new EmailMessage
        {
            MessageId = message.MessageId,
            From = message.From.ToString(),
            To = message.To.Select(x => x.ToString()).ToArray(),
            Cc = message.Cc.Select(x => x.ToString()).ToArray(),
            Bcc = message.Bcc.Select(x => x.ToString()).ToArray(),
            Subject = message.Subject,
            DateUtc = message.Date.ToUniversalTime(),
            TextBody = message.TextBody,
            HtmlBody = message.HtmlBody,
            SourceFormat = "EML",
            Attachments = message.Attachments
                .Select(CreateAttachment)
                .ToArray()
        };
    }

    private static EmailAttachment CreateAttachment(
        MimeEntity entity)
    {
        return new EmailAttachment
        {
            FileName =
                entity.ContentDisposition?.FileName ??
                entity.ContentType.Name ??
                "attachment",
            ContentType = entity.ContentType.MimeType,
            ContentId = entity.ContentId,
            IsInline =
                entity.ContentDisposition?.Disposition ==
                ContentDisposition.Inline
        };
    }
}
