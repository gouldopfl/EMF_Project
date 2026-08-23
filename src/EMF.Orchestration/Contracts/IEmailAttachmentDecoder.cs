using EMF.Orchestration.Models;

namespace EMF.Orchestration.Contracts;

public interface IEmailAttachmentDecoder
{
    Task<IReadOnlyList<DecodedEmailAttachment>> DecodeAsync(
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default);
}
