using EMF.Orchestration.Models;

namespace EMF.Orchestration.Contracts;

public interface IZipArchiveDecoder
{
    Task<IReadOnlyList<DecodedArchiveEntry>> DecodeAsync(
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default);
}
