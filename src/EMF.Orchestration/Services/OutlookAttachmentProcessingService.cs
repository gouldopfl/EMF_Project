using EMF.Core.Models.Identities;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class OutlookAttachmentProcessingService :
    IOutlookAttachmentProcessingService
{
    private readonly IOutlookMessageDecoder _decoder;
    private readonly IEmailAttachmentExtractionService _extractionService;

    public OutlookAttachmentProcessingService(
        IOutlookMessageDecoder decoder,
        IEmailAttachmentExtractionService extractionService)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(extractionService);

        _decoder = decoder;
        _extractionService = extractionService;
    }

    public async Task<IReadOnlyList<EmailAttachmentExtractionResult>> ProcessAsync(
        ArtifactId messageArtifactId,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        var message =
            await _decoder.DecodeAsync(content, cancellationToken);

        var results =
            new List<EmailAttachmentExtractionResult>(
                message.Attachments.Count);

        foreach (var attachment in message.Attachments)
        {
            results.Add(
                await _extractionService.ExtractAsync(
                    messageArtifactId,
                    attachment.FileName,
                    null,
                    attachment.Content,
                    cancellationToken));
        }

        return results;
    }
}
