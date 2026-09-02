using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class EvidenceFileIngestionService :
    IEvidenceFileIngestionService
{
    private readonly IEvidenceRepository _repository;
    private readonly IArtifactContentStore _contentStore;
    private readonly IContentFingerprintService _fingerprintService;
    private readonly IArtifactIdGenerator _artifactIdGenerator;
    private readonly IArtifactFactory _artifactFactory;

    public EvidenceFileIngestionService(
        IEvidenceRepository repository,
        IArtifactContentStore contentStore,
        IContentFingerprintService fingerprintService,
        IArtifactIdGenerator artifactIdGenerator,
        IArtifactFactory artifactFactory)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(fingerprintService);
        ArgumentNullException.ThrowIfNull(artifactIdGenerator);
        ArgumentNullException.ThrowIfNull(artifactFactory);

        _repository = repository;
        _contentStore = contentStore;
        _fingerprintService = fingerprintService;
        _artifactIdGenerator = artifactIdGenerator;
        _artifactFactory = artifactFactory;
    }

    public async Task<EvidenceFileIngestionResult> IngestAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var fullPath = Path.GetFullPath(sourcePath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "Evidence file was not found.",
                fullPath);
        }

        var fingerprint =
            await _fingerprintService.ComputeAsync(
                fullPath,
                cancellationToken);

        var existing =
            await _repository.FindArtifactAsync(
                fullPath,
                fingerprint,
                cancellationToken);

        if (existing is not null)
        {
            var provenance =
                await _repository.GetProvenanceAsync(
                    existing.Id,
                    cancellationToken);

            return new EvidenceFileIngestionResult
            {
                Artifact = existing,
                Provenance = provenance.First(
                    item => item.Source == fullPath),
                AlreadyExisted = true
            };
        }

        var file = new FileInfo(fullPath);
        var artifactId = _artifactIdGenerator.Generate();

        var item =
            new EMF.Discovery.Models.DiscoveredItem
            {
                Name = file.Name,
                SourcePath = fullPath,
                SourceType = "file",
                SizeBytes = file.Length,
                CreatedUtc = file.CreationTimeUtc,
                ModifiedUtc = file.LastWriteTimeUtc
            };

        var creation =
            _artifactFactory.Create(
                item,
                artifactId,
                fingerprint);

        var content =
            await File.ReadAllBytesAsync(
                fullPath,
                cancellationToken);

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
                    "Evidence file persistence failed and content cleanup also failed.",
                    persistenceException,
                    cleanupException);
            }

            throw;
        }

        return new EvidenceFileIngestionResult
        {
            Artifact = creation.Artifact,
            Provenance = creation.Provenance,
            AlreadyExisted = false
        };
    }
}
