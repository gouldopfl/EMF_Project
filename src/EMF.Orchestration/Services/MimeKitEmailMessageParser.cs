using EMF.Discovery.Models.Email;
using EMF.Orchestration.Contracts;
using MimeKit;

namespace EMF.Orchestration.Services;

public sealed class MimeKitEmailMessageParser :
    IEmailMessageParser
{
    public const long DefaultMaxInputBytes =
        100L * 1024 * 1024;

    private readonly long _maxInputBytes;

    public MimeKitEmailMessageParser(
        long maxInputBytes = DefaultMaxInputBytes)
    {
        if (maxInputBytes <= 0 ||
            maxInputBytes > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxInputBytes));
        }

        _maxInputBytes = maxInputBytes;
    }

    public async Task<EmailMessage> ParseAsync(
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (content.Length > _maxInputBytes)
        {
            throw new InvalidDataException(
                "Email message input exceeds the maximum allowed size.");
        }

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
