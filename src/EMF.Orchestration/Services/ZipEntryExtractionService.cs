using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Discovery.Models;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class ZipEntryExtractionService :
    IZipEntryExtractionService
{
    private readonly IEvidenceRepository _repository;
    private readonly IArtifactContentStore _contentStore;
    private readonly IContentFingerprintService _fingerprintService;
    private readonly IArtifactIdGenerator _artifactIdGenerator;
    private readonly IArtifactFactory _artifactFactory;

    public ZipEntryExtractionService(
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

    public async Task<ZipEntryExtractionResult> ExtractAsync(
        ArtifactId archiveArtifactId,
        string entryName,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryName);

        var source =
            $"{archiveArtifactId.Value}/{entryName}";

        var fingerprint =
            await _fingerprintService.ComputeAsync(
                content,
                cancellationToken);

        var existing =
            await _repository.FindArtifactAsync(
                source,
                fingerprint,
                cancellationToken);

        if (existing is not null)
        {
            var provenance =
                await _repository.GetProvenanceAsync(
                    existing.Id,
                    cancellationToken);

            var existingRelationships =
                await _repository.GetRelationshipsAsync(
                    existing.Id,
                    cancellationToken);

            return new ZipEntryExtractionResult
            {
                Artifact = existing,
                Provenance = provenance.First(
                    item => item.Source == source),
                Relationships = existingRelationships
            };
        }

        var artifactId = _artifactIdGenerator.Generate();

        var metadata =
            new Dictionary<string, object>();


        var item =
            new DiscoveredItem
            {
                Name = entryName,
                SourcePath = source,
                SourceType = "zip-entry",
                SizeBytes = content.Length,
                Metadata = metadata
            };

        var creation =
            _artifactFactory.Create(
                item,
                artifactId,
                fingerprint);

        var relationships =
            new[]
            {
                new Relationship
                {
                    SourceArtifactId = archiveArtifactId,
                    TargetArtifactId = artifactId,
                    RelationshipType = RelationshipTypes.Contains
                },
                new Relationship
                {
                    SourceArtifactId = artifactId,
                    TargetArtifactId = archiveArtifactId,
                    RelationshipType = RelationshipTypes.DerivedFrom
                }
            };

        await _contentStore.WriteAsync(
            artifactId,
            content,
            cancellationToken);

        try
        {
            await _repository
                .AddArtifactWithProvenanceAndRelationshipsAsync(
                    creation.Artifact,
                    creation.Provenance,
                    relationships,
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
                    "ZIP entry persistence failed and content cleanup also failed.",
                    persistenceException,
                    cleanupException);
            }

            throw;
        }

        return new ZipEntryExtractionResult
        {
            Artifact = creation.Artifact,
            Provenance = creation.Provenance,
            Relationships = relationships
        };
    }
}
