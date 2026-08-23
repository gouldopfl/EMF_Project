using EMF.Orchestration.Models;

namespace EMF.Orchestration.Contracts;

public interface IOutlookMessageDecoder
{
    Task<DecodedOutlookMessage> DecodeAsync(
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default);
}
