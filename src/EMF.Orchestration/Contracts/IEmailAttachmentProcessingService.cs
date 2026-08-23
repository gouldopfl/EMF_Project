using EMF.Core.Models.Identities;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Contracts;

public interface IEmailAttachmentProcessingService
{
    Task<IReadOnlyList<EmailAttachmentExtractionResult>> ProcessAsync(
        ArtifactId emailArtifactId,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default);
}
