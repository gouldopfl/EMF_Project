using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Discovery.Contracts;
using EMF.Discovery.Models;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class EmailMessageWorkflowActivity :
    IEmailMessageWorkflowActivity
{
    private readonly IStreamingDiscoveryService _discovery;
    private readonly IEvidenceRepository _repository;
    private readonly IArtifactContentStore _contentStore;
    private readonly IContentFingerprintService _fingerprintService;
    private readonly IArtifactIdGenerator _artifactIdGenerator;
    private readonly IArtifactFactory _artifactFactory;
    private readonly string _sourcePath;
    private readonly DiscoveryOptions _options;

    public EmailMessageWorkflowActivity(
        IStreamingDiscoveryService discovery,
        IEvidenceRepository repository,
        IArtifactContentStore contentStore,
        IContentFingerprintService fingerprintService,
        IArtifactIdGenerator artifactIdGenerator,
        IArtifactFactory artifactFactory,
        string sourcePath,
        DiscoveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(fingerprintService);
        ArgumentNullException.ThrowIfNull(artifactIdGenerator);
        ArgumentNullException.ThrowIfNull(artifactFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(options);

        _discovery = discovery;
        _repository = repository;
        _contentStore = contentStore;
        _fingerprintService = fingerprintService;
        _artifactIdGenerator = artifactIdGenerator;
        _artifactFactory = artifactFactory;
        _sourcePath = sourcePath;
        _options = options;
    }

    public string Id => "email-messages";

    public string Name => "Email Messages";

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
            if (!string.Equals(
                    Path.GetExtension(item.Name),
                    ".eml",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                await ProcessEmailAsync(
                    item,
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
                $"Email message processing handled {processed} message(s); {failed} failed.",
            CompletedUtc = DateTimeOffset.UtcNow
        };
    }

    private async Task ProcessEmailAsync(
        DiscoveredItem item,
        CancellationToken cancellationToken)
    {
        var content =
            await File.ReadAllBytesAsync(
                item.SourcePath,
                cancellationToken);

        var fingerprint =
            await _fingerprintService.ComputeAsync(
                content,
                cancellationToken);

        var existing =
            await _repository.FindArtifactAsync(
                item.SourcePath,
                fingerprint,
                cancellationToken);

        if (existing is not null)
            return;

        var artifactId =
            _artifactIdGenerator.Generate();

        var creation =
            _artifactFactory.Create(
                item,
                artifactId,
                fingerprint);

        await _contentStore.WriteAsync(
            artifactId,
            content,
            cancellationToken);

        try
        {
            await _repository.AddArtifactWithProvenanceAsync(
                creation.Artifact,
                creation.Provenance,
                cancellationToken);
        }
        catch (Exception persistenceException)
            when (persistenceException is not OperationCanceledException)
        {
            try
            {
                await _contentStore.DeleteAsync(
                    artifactId,
                    cancellationToken);
            }
            catch (Exception cleanupException)
                when (cleanupException is not OperationCanceledException)
            {
                throw new AggregateException(
                    "Email message persistence failed and content cleanup also failed.",
                    persistenceException,
                    cleanupException);
            }

            throw;
        }
    }
}
