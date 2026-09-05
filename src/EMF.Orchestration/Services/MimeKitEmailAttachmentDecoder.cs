using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;
using MimeKit;

namespace EMF.Orchestration.Services;

public sealed class MimeKitEmailAttachmentDecoder :
    IEmailAttachmentDecoder
{
    public const int DefaultMaxAttachmentCount = 100;
    public const long DefaultMaxAttachmentBytes = 25L * 1024 * 1024;
    public const long DefaultMaxTotalAttachmentBytes = 100L * 1024 * 1024;

    private readonly int _maxAttachmentCount;
    private readonly long _maxAttachmentBytes;
    private readonly long _maxTotalAttachmentBytes;

    public MimeKitEmailAttachmentDecoder(
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

        var attachmentCount = 0;
        long totalAttachmentBytes = 0;

        foreach (var entity in message.Attachments)
        {
            attachmentCount++;

            if (attachmentCount > _maxAttachmentCount)
            {
                throw new InvalidDataException(
                    "Email message exceeds the maximum allowed attachment count.");
            }

            if (entity is not MimePart part)
                continue;

            if (part.Content is null)
                continue;

            var remainingTotalBytes =
                _maxTotalAttachmentBytes -
                totalAttachmentBytes;

            if (remainingTotalBytes <= 0)
            {
                throw new InvalidDataException(
                    "Email attachments exceed the maximum allowed total decoded size.");
            }

            var outputLimit =
                Math.Min(
                    _maxAttachmentBytes,
                    remainingTotalBytes);

            var limitMessage =
                outputLimit == _maxAttachmentBytes
                    ? "Email attachment exceeds the maximum allowed decoded size."
                    : "Email attachments exceed the maximum allowed total decoded size.";

            await using var output =
                new LimitedMemoryStream(
                    outputLimit,
                    limitMessage);

            await part.Content.DecodeToAsync(
                output,
                cancellationToken);

            var decodedContent = output.ToArray();
            totalAttachmentBytes += decodedContent.LongLength;

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
                    Content = decodedContent
                });
        }

        return attachments;
    }
    private sealed class LimitedMemoryStream : MemoryStream
    {
        private readonly long _maxBytes;
        private readonly string _limitMessage;

        public LimitedMemoryStream(
            long maxBytes,
            string limitMessage)
        {
            _maxBytes = maxBytes;
            _limitMessage = limitMessage;
        }

        public override void Write(
            byte[] buffer,
            int offset,
            int count)
        {
            EnsureWithinLimit(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(
            ReadOnlySpan<byte> buffer)
        {
            EnsureWithinLimit(buffer.Length);
            base.Write(buffer);
        }

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            EnsureWithinLimit(count);

            return base.WriteAsync(
                buffer,
                offset,
                count,
                cancellationToken);
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            EnsureWithinLimit(buffer.Length);

            return base.WriteAsync(
                buffer,
                cancellationToken);
        }

        public override void WriteByte(byte value)
        {
            EnsureWithinLimit(1);
            base.WriteByte(value);
        }

        public override void SetLength(long value)
        {
            if (value > _maxBytes)
                throw new InvalidDataException(_limitMessage);

            base.SetLength(value);
        }

        private void EnsureWithinLimit(int count)
        {
            if (Position > _maxBytes - count)
                throw new InvalidDataException(_limitMessage);
        }
    }

}
