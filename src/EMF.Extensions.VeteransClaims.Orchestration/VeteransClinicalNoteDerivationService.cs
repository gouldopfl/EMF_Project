using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Discovery.Models;
using EMF.Extensions.VeteransClaims.Models;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Services;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransClinicalNoteDerivationResult
{
    public required Artifact Artifact { get; init; }
    public required Provenance Provenance { get; init; }
    public required IReadOnlyList<Relationship> Relationships { get; init; }
}

public sealed class VeteransClinicalNoteDerivationService
{
    public const int DefaultMaxContentBytes =
        2 * 1024 * 1024;

    private readonly IEvidenceRepository _repository;
    private readonly IArtifactContentStore _contentStore;
    private readonly IContentFingerprintService _fingerprints;
    private readonly IArtifactIdGenerator _ids;
    private readonly IArtifactFactory _factory;
    private readonly int _maxContentBytes;

    public VeteransClinicalNoteDerivationService(
        IEvidenceRepository repository,
        IArtifactContentStore contentStore,
        IContentFingerprintService fingerprints,
        IArtifactIdGenerator ids,
        IArtifactFactory factory,
        int maxContentBytes = DefaultMaxContentBytes)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(fingerprints);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(factory);

        if (maxContentBytes <= 0 ||
            maxContentBytes > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxContentBytes));
        }

        _repository = repository;
        _contentStore = contentStore;
        _fingerprints = fingerprints;
        _ids = ids;
        _factory = factory;
        _maxContentBytes = maxContentBytes;
    }

    public async Task<VeteransClinicalNoteDerivationResult> DeriveAsync(
        ArtifactId parentArtifactId,
        string fileName,
        int sourceStartPage,
        int sourceEndPage,
        DateOnly noteDate,
        string noteTitle,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(noteTitle);

        if (content.IsEmpty)
            throw new InvalidDataException(
                "Clinical note content must not be empty.");

        if (content.Length > _maxContentBytes)
            throw new InvalidDataException(
                "Clinical note content exceeds the maximum allowed size.");

        if (sourceStartPage <= 0 || sourceEndPage < sourceStartPage)
            throw new ArgumentOutOfRangeException(nameof(sourceStartPage));

        var parent =
            await _repository.GetArtifactAsync(
                parentArtifactId,
                cancellationToken);

        if (parent is null)
            throw new InvalidOperationException(
                $"Parent artifact '{parentArtifactId.Value}' does not exist.");

        var source =
            $"{parentArtifactId.Value}/clinical-note/" +
            $"{sourceStartPage}-{sourceEndPage}/{fileName}";

        var fingerprint =
            await _fingerprints.ComputeAsync(content, cancellationToken);

        var existing =
            await _repository.FindArtifactAsync(
                source, fingerprint, cancellationToken);

        if (existing is not null)
        {
            var provenance =
                await _repository.GetProvenanceAsync(
                    existing.Id, cancellationToken);
            var existingRelationships =
                await _repository.GetRelationshipsAsync(
                    existing.Id, cancellationToken);

            return new VeteransClinicalNoteDerivationResult
            {
                Artifact = existing,
                Provenance = provenance.First(x => x.Source == source),
                Relationships = existingRelationships
            };
        }

        var artifactId = _ids.Generate();

        var metadata = new Dictionary<string, object>
        {
            [VeteransArtifactMetadataKeys.SourceStartPage] =
                sourceStartPage.ToString(),
            [VeteransArtifactMetadataKeys.SourceEndPage] =
                sourceEndPage.ToString(),
            [VeteransArtifactMetadataKeys.NoteDate] =
                noteDate.ToString("yyyy-MM-dd"),
            [VeteransArtifactMetadataKeys.NoteTitle] = noteTitle,
            [VeteransArtifactMetadataKeys.EvidenceDate] =
                noteDate.ToString("yyyy-MM-dd"),
            [VeteransArtifactMetadataKeys.EvidenceTitle] = noteTitle,
            [ArtifactMetadataKeys.ContentType] = "text/plain"
        };

        var creation =
            _factory.Create(
                new DiscoveredItem
                {
                    Name = fileName,
                    SourcePath = source,
                    SourceType = "veterans-clinical-note",
                    SizeBytes = content.Length,
                    Metadata = metadata
                },
                artifactId,
                fingerprint);

        if (creation is null ||
            creation.Artifact is null ||
            creation.Artifact.Id != artifactId)
        {
            throw new InvalidOperationException(
                "Clinical note factory returned an invalid artifact identity.");
        }

        if (creation.Artifact.Fingerprint is null ||
            creation.Artifact.Fingerprint != fingerprint)
        {
            throw new InvalidOperationException(
                "Clinical note factory returned an invalid content fingerprint.");
        }

        if (creation.Provenance is null ||
            creation.Provenance.ArtifactId != artifactId ||
            !string.Equals(
                creation.Provenance.Source,
                source,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Clinical note factory returned invalid provenance.");
        }

        var relationships = new[]
        {
            new Relationship
            {
                SourceArtifactId = parentArtifactId,
                TargetArtifactId = artifactId,
                RelationshipType = RelationshipTypes.Contains
            },
            new Relationship
            {
                SourceArtifactId = artifactId,
                TargetArtifactId = parentArtifactId,
                RelationshipType = RelationshipTypes.DerivedFrom
            }
        };

        await _contentStore.WriteAsync(
            artifactId, content, cancellationToken);

        try
        {
            await _repository.AddArtifactWithProvenanceAndRelationshipsAsync(
                creation.Artifact,
                creation.Provenance,
                relationships,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            try
            {
                await _contentStore.DeleteAsync(
                    artifactId, cancellationToken);
            }
            catch (Exception cleanup)
                when (cleanup is not OperationCanceledException)
            {
                throw new AggregateException(
                    "Clinical note persistence failed and content cleanup also failed.",
                    ex,
                    cleanup);
            }

            throw;
        }

        return new VeteransClinicalNoteDerivationResult
        {
            Artifact = creation.Artifact,
            Provenance = creation.Provenance,
            Relationships = relationships
        };
    }
}
