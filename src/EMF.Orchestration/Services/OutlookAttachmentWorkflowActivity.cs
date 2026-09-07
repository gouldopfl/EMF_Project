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
    private const string ProcessorId = "outlook-attachment";
    private const string ProcessorVersion = "1";

    private readonly ContainerAncestryGuard _ancestryGuard;
    private readonly ContainerProcessingGuard _processingGuard;

    public OutlookAttachmentWorkflowActivity(
        IEvidenceRepository repository,
        IArtifactContentStore contentStore,
        IOutlookAttachmentProcessingService processingService,
        ContainerProcessingGuard processingGuard,
        ContainerAncestryGuard? ancestryGuard = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(processingService);
        ArgumentNullException.ThrowIfNull(processingGuard);

        _repository = repository;
        _contentStore = contentStore;
        _processingService = processingService;
        _processingGuard = processingGuard;
        _ancestryGuard =
            ancestryGuard ??
            new ContainerAncestryGuard(repository);
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
                await _ancestryGuard.ValidateAsync(
                    message,
                    cancellationToken);

                var decision =
                    await _processingGuard.EvaluateAsync(
                        message,
                        content,
                        ProcessorId,
                        ProcessorVersion,
                        cancellationToken);

                if (!decision.ShouldProcess)
                    continue;

                await _processingService.ProcessAsync(
                    message.Id,
                    content,
                    cancellationToken);

                await _processingGuard.MarkProcessedAsync(
                    message,
                    decision.Fingerprint,
                    ProcessorId,
                    ProcessorVersion,
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
