using EMF.Core.Models.Identities;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Contracts;

public interface IZipEntryExtractionService
{
    Task<ZipEntryExtractionResult> ExtractAsync(
        ArtifactId archiveArtifactId,
        string entryName,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default);
}
