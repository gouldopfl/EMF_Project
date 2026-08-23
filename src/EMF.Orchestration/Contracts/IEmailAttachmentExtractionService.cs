using EMF.Core.Models.Identities;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Contracts;

public interface IEmailAttachmentExtractionService
{
    Task<EmailAttachmentExtractionResult> ExtractAsync(
        ArtifactId emailArtifactId,
        string fileName,
        string? contentType,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default);
}
