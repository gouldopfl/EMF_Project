using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;
using MimeKit;

namespace EMF.Orchestration.Services;

public sealed class MimeKitEmailAttachmentDecoder :
    IEmailAttachmentDecoder
{
    public async Task<IReadOnlyList<DecodedEmailAttachment>> DecodeAsync(
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        await using var stream =
            new MemoryStream(content.ToArray(), writable: false);

        var message =
            await MimeMessage.LoadAsync(
                stream,
                cancellationToken);

        var attachments =
            new List<DecodedEmailAttachment>();

        foreach (var entity in message.Attachments)
        {
            if (entity is not MimePart part)
                continue;

            if (part.Content is null)
                continue;

            await using var output = new MemoryStream();

            await part.Content.DecodeToAsync(
                output,
                cancellationToken);

            attachments.Add(
                new DecodedEmailAttachment
                {
                    FileName =
                        part.FileName ??
                        part.ContentType.Name ??
                        "attachment",
                    ContentType = part.ContentType.MimeType,
                    ContentId = part.ContentId,
                    IsInline =
                        part.ContentDisposition?.Disposition ==
                        ContentDisposition.Inline,
                    Content = output.ToArray()
                });
        }

        return attachments;
    }
}
