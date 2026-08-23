using EMF.Core.Models.Identities;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Contracts;

public interface IZipArchiveProcessingService
{
    Task<IReadOnlyList<ZipEntryExtractionResult>> ProcessAsync(
        ArtifactId archiveArtifactId,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default);
}
