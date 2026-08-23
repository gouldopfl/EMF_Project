using MsgReader.Outlook;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class OutlookMessageDecoder :
    IOutlookMessageDecoder
{
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
            message.Attachments
                .OfType<Storage.Attachment>()
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item.FileName) &&
                    item.Data is not null)
                .Select(item =>
                    new DecodedOutlookAttachment
                    {
                        FileName = item.FileName!,
                        Content = item.Data!,
                        ContentId = item.ContentId,
                        IsInline = item.IsInline
                    })
                .ToArray();

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
