using EMF.Orchestration.Models;

namespace EMF.Orchestration.Contracts;

public interface IZipArchiveDecoder
{
    Task<IReadOnlyList<DecodedArchiveEntry>> DecodeAsync(
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DecodedArchiveEntry>> DecodeAsync(Stream content,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Seekable ZIP processing capability is required.");
}
