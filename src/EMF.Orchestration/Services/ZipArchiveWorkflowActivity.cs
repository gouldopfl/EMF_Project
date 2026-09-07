using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class ZipArchiveWorkflowActivity :
    IZipArchiveWorkflowActivity
{
    private readonly IEvidenceRepository _repository;
    private readonly IArtifactContentStore _contentStore;
    private readonly IZipArchiveProcessingService _processingService;
    private const string ProcessorId = "zip-archive";
    private const string ProcessorVersion = "1";

    private readonly ContainerAncestryGuard _ancestryGuard;
    private readonly ContainerProcessingGuard _processingGuard;

    public ZipArchiveWorkflowActivity(
        IEvidenceRepository repository,
        IArtifactContentStore contentStore,
        IZipArchiveProcessingService processingService,
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

    public string Id => "zip-archives";

    public string Name => "ZIP Archives";

    public async Task<WorkflowActivityResult> ExecuteAsync(
        WorkflowExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var archives =
            await _repository.GetArtifactsByMetadataAsync(
                ArtifactMetadataKeys.FileExtension,
                ".zip",
                cancellationToken);

        var processed = 0;
        var failed = 0;

        foreach (var archive in archives)
        {
            var content =
                await _contentStore.ReadAsync(
                    archive.Id,
                    cancellationToken);

            if (content is null)
            {
                failed++;
                continue;
            }

            try
            {
                await _ancestryGuard.ValidateAsync(
                    archive,
                    cancellationToken);

                var decision =
                    await _processingGuard.EvaluateAsync(
                        archive,
                        content,
                        ProcessorId,
                        ProcessorVersion,
                        cancellationToken);

                if (!decision.ShouldProcess)
                    continue;

                await _processingService.ProcessAsync(
                    archive.Id,
                    content,
                    cancellationToken);

                await _processingGuard.MarkProcessedAsync(
                    archive,
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
                $"ZIP processing handled {processed} archive(s); {failed} failed.",
            CompletedUtc = DateTimeOffset.UtcNow
        };
    }
}
