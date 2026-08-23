using EMF.Core.Models.Identities;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class EmailAttachmentProcessingService :
    IEmailAttachmentProcessingService
{
    private readonly IEmailAttachmentDecoder _decoder;
    private readonly IEmailAttachmentExtractionService _extractionService;

    public EmailAttachmentProcessingService(
        IEmailAttachmentDecoder decoder,
        IEmailAttachmentExtractionService extractionService)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(extractionService);

        _decoder = decoder;
        _extractionService = extractionService;
    }

    public async Task<IReadOnlyList<EmailAttachmentExtractionResult>> ProcessAsync(
        ArtifactId emailArtifactId,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        var attachments =
            await _decoder.DecodeAsync(
                content,
                cancellationToken);

        var results =
            new List<EmailAttachmentExtractionResult>(
                attachments.Count);

        foreach (var attachment in attachments)
        {
            results.Add(
                await _extractionService.ExtractAsync(
                    emailArtifactId,
                    attachment.FileName,
                    attachment.ContentType,
                    attachment.Content,
                    cancellationToken));
        }

        return results;
    }
}
