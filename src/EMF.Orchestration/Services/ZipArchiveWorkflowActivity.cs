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
    private readonly ZipParentAllocationProfile _profile;
    private const string ProcessorId = "zip-archive";
    private const string ProcessorVersion = "1";

    private readonly ContainerAncestryGuard _ancestryGuard;
    private readonly ContainerProcessingGuard _processingGuard;

    public ZipArchiveWorkflowActivity(
        IEvidenceRepository repository,
        IArtifactContentStore contentStore,
        IZipArchiveProcessingService processingService,
        ContainerProcessingGuard processingGuard,
        ContainerAncestryGuard? ancestryGuard = null,
        ZipParentAllocationProfile? allocationProfile = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(processingService);
        ArgumentNullException.ThrowIfNull(processingGuard);

        _profile = allocationProfile ?? new();
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

        var boundedStore = _contentStore as IBoundedVersionedArtifactContentStore
            ?? throw new NotSupportedException("ZIP requires bounded content reads; ordinary reads are not a fallback.");
        _profile.ValidateProviderMaximum(boundedStore.MaximumStoredRepresentationBytes);

        var archives =
            await _repository.GetArtifactsByMetadataAsync(
                ArtifactMetadataKeys.FileExtension,
                ".zip",
                cancellationToken);

        var processed = 0;
        var failed = 0;

        foreach (var archive in archives)
        {
            try
            {
                await _ancestryGuard.ValidateAsync(archive, cancellationToken);
                ContainerProcessingDecision decision;
                using (await ZipParentReadAdmission.ProcessWide.AcquireAsync(cancellationToken))
                {
                    await using var lease = await boundedStore.ReadBoundedVersionedAsync(
                        archive.Id, _profile.CreateReadRequest(), cancellationToken);
                    if (lease is null) { failed++; continue; }
                    if (lease.ArtifactId != archive.Id || lease.Content.Length != lease.ReturnedLength ||
                        lease.ReturnedLength > _profile.MaximumPlaintextBytes ||
                        lease.StoredLength > _profile.MaximumProtectedSourceBytes)
                        throw new InvalidDataException("ZIP parent lease exceeds its admitted profile.");
                    decision = await _processingGuard.EvaluateAsync(archive, lease.Content,
                        ProcessorId, ProcessorVersion, cancellationToken);
                    if (!decision.ShouldProcess) continue;
                    await using var stream = lease.OpenReadStream();
                    await _processingService.ProcessAsync(archive.Id, stream, cancellationToken);
                } // Stream then lease are disposed/cleared before admission is released.

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
