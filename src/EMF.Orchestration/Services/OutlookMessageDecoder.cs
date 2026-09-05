using MsgReader.Outlook;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class OutlookMessageDecoder :
    IOutlookMessageDecoder
{
    public const int DefaultMaxAttachmentCount = 100;
    public const long DefaultMaxAttachmentBytes = 25L * 1024 * 1024;
    public const long DefaultMaxTotalAttachmentBytes = 100L * 1024 * 1024;

    private readonly int _maxAttachmentCount;
    private readonly long _maxAttachmentBytes;
    private readonly long _maxTotalAttachmentBytes;

    public OutlookMessageDecoder(
        int maxAttachmentCount = DefaultMaxAttachmentCount,
        long maxAttachmentBytes = DefaultMaxAttachmentBytes,
        long maxTotalAttachmentBytes =
            DefaultMaxTotalAttachmentBytes)
    {
        if (maxAttachmentCount <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxAttachmentCount));

        if (maxAttachmentBytes <= 0 ||
            maxAttachmentBytes > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxAttachmentBytes));
        }

        if (maxTotalAttachmentBytes <= 0 ||
            maxTotalAttachmentBytes > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxTotalAttachmentBytes));
        }

        _maxAttachmentCount = maxAttachmentCount;
        _maxAttachmentBytes = maxAttachmentBytes;
        _maxTotalAttachmentBytes = maxTotalAttachmentBytes;
    }

    public Task<DecodedOutlookMessage> DecodeAsync(
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        System.Text.Encoding.RegisterProvider(
            System.Text.CodePagesEncodingProvider.Instance);

        using var stream =
            new MemoryStream(content.ToArray(), writable: false);

        using var message =
            new Storage.Message(
                stream,
                FileAccess.Read,
                false);

        var attachments =
            new List<DecodedOutlookAttachment>();

        var attachmentCount = 0;
        long totalAttachmentBytes = 0;

        foreach (var item in message.Attachments)
        {
            if (item is not Storage.Attachment attachment)
                continue;

            attachmentCount++;

            if (attachmentCount > _maxAttachmentCount)
            {
                throw new InvalidDataException(
                    "Outlook message exceeds the maximum allowed attachment count.");
            }

            var data = attachment.Data;

            if (string.IsNullOrWhiteSpace(attachment.FileName) ||
                data is null)
            {
                continue;
            }

            if (data.LongLength > _maxAttachmentBytes)
            {
                throw new InvalidDataException(
                    "Outlook attachment exceeds the maximum allowed decoded size.");
            }

            if (data.LongLength >
                _maxTotalAttachmentBytes - totalAttachmentBytes)
            {
                throw new InvalidDataException(
                    "Outlook attachments exceed the maximum allowed total decoded size.");
            }

            totalAttachmentBytes += data.LongLength;

            attachments.Add(
                new DecodedOutlookAttachment
                {
                    FileName = attachment.FileName,
                    Content = data,
                    ContentId = attachment.ContentId,
                    IsInline = attachment.IsInline
                });
        }

        return Task.FromResult(
            new DecodedOutlookMessage
            {
                Subject = message.Subject,
                BodyText = message.BodyText,
                BodyHtml = message.BodyHtml,
                Attachments = attachments
            });
    }
}
