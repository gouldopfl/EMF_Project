using EMF.Core.Models.Identities;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Contracts;

public interface IOutlookAttachmentProcessingService
{
    Task<IReadOnlyList<EmailAttachmentExtractionResult>> ProcessAsync(
        ArtifactId messageArtifactId,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default);
}
