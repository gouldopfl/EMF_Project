using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class EvidenceFileIngestionService :
    IEvidenceFileIngestionService
{
    public const long DefaultMaxFileBytes =
        100L * 1024 * 1024;

    private readonly IEvidenceRepository _repository;
    private readonly IArtifactContentStore _contentStore;
    private readonly IContentFingerprintService _fingerprintService;
    private readonly IArtifactIdGenerator _artifactIdGenerator;
    private readonly IArtifactFactory _artifactFactory;
    private readonly long _maxFileBytes;

    public EvidenceFileIngestionService(
        IEvidenceRepository repository,
        IArtifactContentStore contentStore,
        IContentFingerprintService fingerprintService,
        IArtifactIdGenerator artifactIdGenerator,
        IArtifactFactory artifactFactory,
        long maxFileBytes = DefaultMaxFileBytes)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(fingerprintService);
        ArgumentNullException.ThrowIfNull(artifactIdGenerator);
        ArgumentNullException.ThrowIfNull(artifactFactory);

        if (maxFileBytes <= 0 ||
            maxFileBytes > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxFileBytes));
        }

        _repository = repository;
        _contentStore = contentStore;
        _fingerprintService = fingerprintService;
        _artifactIdGenerator = artifactIdGenerator;
        _artifactFactory = artifactFactory;
        _maxFileBytes = maxFileBytes;
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

        var file = new FileInfo(fullPath);

        if (file.Length > _maxFileBytes)
            throw new InvalidDataException(
                "Evidence file exceeds the maximum allowed size.");

        await using var stream = File.OpenRead(fullPath);

        var length = stream.Length;

        if (length > _maxFileBytes)
            throw new InvalidDataException(
                "Evidence file exceeds the maximum allowed size.");

        var content = new byte[(int)length];

        await stream.ReadExactlyAsync(
            content,
            cancellationToken);

        if (stream.Position != stream.Length)
            throw new IOException(
                "Evidence file changed during ingestion.");

        var fingerprint =
            await _fingerprintService.ComputeAsync(
                content,
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

        var artifactId = _artifactIdGenerator.Generate();

        var item =
            new EMF.Discovery.Models.DiscoveredItem
            {
                Name = file.Name,
                SourcePath = fullPath,
                SourceType = "file",
                SizeBytes = content.LongLength,
                CreatedUtc = file.CreationTimeUtc,
                ModifiedUtc = file.LastWriteTimeUtc
            };

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
