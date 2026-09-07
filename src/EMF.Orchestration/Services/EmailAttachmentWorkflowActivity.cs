using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class EmailAttachmentWorkflowActivity :
    IWorkflowActivity
{
    private readonly IEvidenceRepository _repository;
    private readonly IArtifactContentStore _contentStore;
    private readonly IEmailAttachmentProcessingService _processingService;
    private const string ProcessorId = "email-attachment";
    private const string ProcessorVersion = "1";

    private readonly ContainerAncestryGuard _ancestryGuard;
    private readonly ContainerProcessingGuard _processingGuard;

    public EmailAttachmentWorkflowActivity(
        IEvidenceRepository repository,
        IArtifactContentStore contentStore,
        IEmailAttachmentProcessingService processingService,
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

    public string Id => "email-attachments";

    public string Name => "Email Attachments";

    public async Task<WorkflowActivityResult> ExecuteAsync(
        WorkflowExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var emails =
            await _repository.GetArtifactsByMetadataAsync(
                ArtifactMetadataKeys.FileExtension,
                ".eml",
                cancellationToken);

        var processed = 0;
        var failed = 0;

        foreach (var email in emails)
        {
            var content =
                await _contentStore.ReadAsync(
                    email.Id,
                    cancellationToken);

            if (content is null)
            {
                failed++;
                continue;
            }

            try
            {
                await _ancestryGuard.ValidateAsync(
                    email,
                    cancellationToken);

                var decision =
                    await _processingGuard.EvaluateAsync(
                        email,
                        content,
                        ProcessorId,
                        ProcessorVersion,
                        cancellationToken);

                if (!decision.ShouldProcess)
                    continue;

                await _processingService.ProcessAsync(
                    email.Id,
                    content,
                    cancellationToken);

                await _processingGuard.MarkProcessedAsync(
                    email,
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
                $"Email attachment processing handled {processed} message(s); {failed} failed.",
            CompletedUtc = DateTimeOffset.UtcNow
        };
    }
}
