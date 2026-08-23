using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class OutlookAttachmentWorkflowActivity :
    IOutlookAttachmentWorkflowActivity
{
    private readonly IEvidenceRepository _repository;
    private readonly IArtifactContentStore _contentStore;
    private readonly IOutlookAttachmentProcessingService _processingService;

    public OutlookAttachmentWorkflowActivity(
        IEvidenceRepository repository,
        IArtifactContentStore contentStore,
        IOutlookAttachmentProcessingService processingService)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(processingService);

        _repository = repository;
        _contentStore = contentStore;
        _processingService = processingService;
    }

    public string Id => "outlook-attachments";

    public string Name => "Outlook Attachments";

    public async Task<WorkflowActivityResult> ExecuteAsync(
        WorkflowExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var messages =
            await _repository.GetArtifactsByMetadataAsync(
                ArtifactMetadataKeys.FileExtension,
                ".msg",
                cancellationToken);

        var processed = 0;
        var failed = 0;

        foreach (var message in messages)
        {
            var content =
                await _contentStore.ReadAsync(
                    message.Id,
                    cancellationToken);

            if (content is null)
            {
                failed++;
                continue;
            }

            try
            {
                await _processingService.ProcessAsync(
                    message.Id,
                    content,
                    cancellationToken);

                processed++;
            }
            catch (Exception ex)
                when (ex is not OperationCanceledException)
            {
                failed++;
            }
        }

        return new WorkflowActivityResult
        {
            Succeeded = failed == 0,
            Message =
                $"Outlook attachment processing handled {processed} message(s); {failed} failed.",
            CompletedUtc = DateTimeOffset.UtcNow
        };
    }
}
