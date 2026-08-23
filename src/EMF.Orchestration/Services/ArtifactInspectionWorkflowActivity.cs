using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Discovery.Contracts;
using EMF.Discovery.Models;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class ArtifactInspectionWorkflowActivity :
    IWorkflowActivity
{
    private readonly IStreamingDiscoveryService _discovery;
    private readonly IArtifactInspectionService _inspection;
    private readonly IEvidenceRepository _repository;
    private readonly IContentFingerprintService _fingerprintService;
    private readonly string _sourcePath;
    private readonly DiscoveryOptions _options;

    public ArtifactInspectionWorkflowActivity(
        IStreamingDiscoveryService discovery,
        IArtifactInspectionService inspection,
        IEvidenceRepository repository,
        IContentFingerprintService fingerprintService,
        string sourcePath,
        DiscoveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(fingerprintService);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(options);

        _discovery = discovery;
        _inspection = inspection;
        _repository = repository;
        _fingerprintService = fingerprintService;
        _sourcePath = sourcePath;
        _options = options;
    }

    public string Id => "artifact-inspection";

    public string Name => "Artifact Inspection";

    public async Task<WorkflowActivityResult> ExecuteAsync(
        WorkflowExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var processed = 0;
        var failed = 0;

        await foreach (var item in _discovery.DiscoverItemsAsync(
            _sourcePath,
            _options,
            cancellationToken))
        {
            try
            {
                var fingerprint =
                    await _fingerprintService.ComputeAsync(
                        item.SourcePath,
                        cancellationToken);

                var artifact =
                    await _repository.FindArtifactAsync(
                        item.SourcePath,
                        fingerprint,
                        cancellationToken);

                if (artifact is null)
                {
                    failed++;
                    continue;
                }

                var inspection =
                    await _inspection.InspectAsync(
                        item.SourcePath,
                        cancellationToken);

                var metadata =
                    new Dictionary<string, object>(
                        inspection.Metadata);

                if (!string.IsNullOrWhiteSpace(
                        inspection.DetectedContentType))
                {
                    metadata[ArtifactMetadataKeys.ContentType] =
                        inspection.DetectedContentType;
                }

                if (!string.IsNullOrWhiteSpace(
                        inspection.DetectedFormat))
                {
                    metadata["detectedFormat"] =
                        inspection.DetectedFormat;
                }

                if (inspection.Findings.Count > 0)
                    metadata["inspectionFindings"] =
                        inspection.Findings.ToArray();

                if (inspection.Limitations.Count > 0)
                    metadata["inspectionLimitations"] =
                        inspection.Limitations.ToArray();

                await _repository.MergeArtifactMetadataAsync(
                    artifact.Id,
                    metadata,
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
                $"Artifact inspection handled {processed} artifact(s); {failed} failed.",
            CompletedUtc = DateTimeOffset.UtcNow
        };
    }
}
