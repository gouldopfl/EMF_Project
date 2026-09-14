using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Discovery.Models;
using EMF.Extensions.VeteransClaims.Models;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Services;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransBoundedEvidenceDerivationResult
{
    public required Artifact Artifact { get; init; }

    public required Provenance Provenance { get; init; }

    public required IReadOnlyList<Relationship> Relationships { get; init; }

    public bool AlreadyExisted { get; init; }
}

public sealed class VeteransBoundedEvidenceDerivationService
{
    public const int DefaultMaxContentBytes =
        512 * 1024;

    private readonly IEvidenceRepository _repository;
    private readonly IArtifactContentStore _contentStore;
    private readonly IContentFingerprintService _fingerprints;
    private readonly IArtifactIdGenerator _ids;
    private readonly IArtifactFactory _factory;
    private readonly int _maxContentBytes;

    public VeteransBoundedEvidenceDerivationService(
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

    public async Task<VeteransBoundedEvidenceDerivationResult> DeriveAsync(
        ArtifactId parentArtifactId,
        ClaimIssueId claimIssueId,
        ServiceConnectionBasisId basisId,
        IReadOnlyCollection<RequirementId> requirementIds,
        int sourceStartPage,
        int sourceEndPage,
        int sourceStartLine,
        int sourceEndLine,
        DateOnly evidenceDate,
        string evidenceTitle,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requirementIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceTitle);

        if (content.IsEmpty)
        {
            throw new InvalidDataException(
                "Bounded evidence content must not be empty.");
        }

        if (content.Length > _maxContentBytes)
        {
            throw new InvalidDataException(
                "Bounded evidence content exceeds the maximum allowed size.");
        }

        if (sourceStartPage <= 0 ||
            sourceEndPage < sourceStartPage)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceStartPage));
        }

        if (sourceStartLine <= 0 ||
            sourceEndLine < sourceStartLine)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceStartLine));
        }

        var normalizedRequirementIds =
            requirementIds
                .Select(id => id.Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

        if (normalizedRequirementIds.Length == 0)
        {
            throw new InvalidDataException(
                "Bounded evidence must identify at least one requirement.");
        }

        var parent =
            await _repository.GetArtifactAsync(
                parentArtifactId,
                cancellationToken);

        if (parent is null)
        {
            throw new InvalidOperationException(
                $"Parent artifact '{parentArtifactId.Value}' does not exist.");
        }

        var requirementKey =
            string.Join(",", normalizedRequirementIds);

        var source =
            $"{parentArtifactId.Value}/bounded-evidence/" +
            $"{claimIssueId.Value}/{basisId.Value}/" +
            $"{sourceStartPage}-{sourceEndPage}/" +
            $"{sourceStartLine}-{sourceEndLine}/" +
            requirementKey;

        var fingerprint =
            await _fingerprints.ComputeAsync(
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

            return new VeteransBoundedEvidenceDerivationResult
            {
                Artifact = existing,
                Provenance = provenance.First(x => x.Source == source),
                Relationships = existingRelationships,
                AlreadyExisted = true
            };
        }

        var artifactId = _ids.Generate();
        var fileName =
            $"bounded-evidence-{sourceStartPage}-{sourceEndPage}-" +
            $"{sourceStartLine}-{sourceEndLine}.txt";

        var metadata = new Dictionary<string, object>
        {
            [VeteransArtifactMetadataKeys.SourceStartPage] =
                sourceStartPage.ToString(),
            [VeteransArtifactMetadataKeys.SourceEndPage] =
                sourceEndPage.ToString(),
            [VeteransArtifactMetadataKeys.SourceStartLine] =
                sourceStartLine.ToString(),
            [VeteransArtifactMetadataKeys.SourceEndLine] =
                sourceEndLine.ToString(),
            [VeteransArtifactMetadataKeys.ClaimIssueId] =
                claimIssueId.Value,
            [VeteransArtifactMetadataKeys.ServiceConnectionBasisId] =
                basisId.Value,
            [VeteransArtifactMetadataKeys.RequirementIds] =
                normalizedRequirementIds,
            [VeteransArtifactMetadataKeys.EvidenceDate] =
                evidenceDate.ToString("yyyy-MM-dd"),
            [VeteransArtifactMetadataKeys.EvidenceTitle] =
                evidenceTitle,
            [ArtifactMetadataKeys.ContentType] = "text/plain"
        };

        var creation =
            _factory.Create(
                new DiscoveredItem
                {
                    Name = fileName,
                    SourcePath = source,
                    SourceType = "veterans-bounded-evidence",
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
                "Bounded evidence factory returned an invalid artifact identity.");
        }

        if (creation.Artifact.Fingerprint is null ||
            creation.Artifact.Fingerprint != fingerprint)
        {
            throw new InvalidOperationException(
                "Bounded evidence factory returned an invalid content fingerprint.");
        }

        if (creation.Provenance is null ||
            creation.Provenance.ArtifactId != artifactId ||
            !string.Equals(
                creation.Provenance.Source,
                source,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Bounded evidence factory returned invalid provenance.");
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
            artifactId,
            content,
            cancellationToken);

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
                    artifactId,
                    cancellationToken);
            }
            catch (Exception cleanup)
                when (cleanup is not OperationCanceledException)
            {
                throw new AggregateException(
                    "Bounded evidence persistence failed and content cleanup also failed.",
                    ex,
                    cleanup);
            }

            throw;
        }

        return new VeteransBoundedEvidenceDerivationResult
        {
            Artifact = creation.Artifact,
            Provenance = creation.Provenance,
            Relationships = relationships,
            AlreadyExisted = false
        };
    }
}
