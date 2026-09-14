using System.Text.Json;
using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransBoundedEvidenceSelection
{
    public required Artifact Artifact { get; init; }

    public required ArtifactId SourceArtifactId { get; init; }

    public required ClaimIssueId ClaimIssueId { get; init; }

    public required ServiceConnectionBasisId BasisId { get; init; }

    public required IReadOnlyList<RequirementId> RequirementIds { get; init; }

    public required int SourceStartPage { get; init; }

    public required int SourceEndPage { get; init; }

    public required int SourceStartLine { get; init; }

    public required int SourceEndLine { get; init; }

    public required DateOnly EvidenceDate { get; init; }

    public required string EvidenceTitle { get; init; }
}

public sealed class VeteransBoundedEvidenceSelectionService
{
    public const string BoundedEvidenceArtifactType =
        "veterans-bounded-evidence";

    private readonly IEvidenceRepository _repository;

    public VeteransBoundedEvidenceSelectionService(
        IEvidenceRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task<IReadOnlyList<VeteransBoundedEvidenceSelection>>
        GetAsync(
            ClaimIssueId claimIssueId,
            ServiceConnectionBasisId basisId,
            RequirementId requirementId,
            ArtifactId sourceArtifactId,
            CancellationToken cancellationToken = default)
    {
        var sourceArtifact =
            await _repository.GetArtifactAsync(
                sourceArtifactId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"Source artifact '{sourceArtifactId.Value}' was not found.");

        if (sourceArtifact.Id != sourceArtifactId)
        {
            throw new InvalidOperationException(
                "Bounded evidence source artifact identity mismatch.");
        }

        var candidates =
            await _repository.GetArtifactsByMetadataAsync(
                VeteransArtifactMetadataKeys.ClaimIssueId,
                claimIssueId.Value,
                cancellationToken);

        var results = new List<VeteransBoundedEvidenceSelection>();

        foreach (var artifact in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.Equals(
                    artifact.ArtifactType,
                    BoundedEvidenceArtifactType,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var artifactClaimIssueId =
                new ClaimIssueId(
                    GetRequiredMetadataText(
                        artifact,
                        VeteransArtifactMetadataKeys.ClaimIssueId));

            if (artifactClaimIssueId != claimIssueId)
            {
                throw new InvalidOperationException(
                    "Bounded evidence claim issue metadata mismatch.");
            }

            var artifactBasisId =
                new ServiceConnectionBasisId(
                    GetRequiredMetadataText(
                        artifact,
                        VeteransArtifactMetadataKeys.ServiceConnectionBasisId));

            if (artifactBasisId != basisId)
                continue;

            var requirementIds =
                GetRequiredRequirementIds(artifact);

            if (!requirementIds.Contains(requirementId))
                continue;

            var relationships =
                await _repository.GetRelationshipsAsync(
                    artifact.Id,
                    cancellationToken);

            var derivedFrom =
                relationships
                    .Where(relationship =>
                        relationship.SourceArtifactId == artifact.Id &&
                        string.Equals(
                            relationship.RelationshipType,
                            RelationshipTypes.DerivedFrom,
                            StringComparison.Ordinal))
                    .ToArray();

            if (derivedFrom.Length != 1)
            {
                throw new InvalidOperationException(
                    "Bounded evidence must have exactly one DerivedFrom relationship.");
            }

            if (derivedFrom[0].TargetArtifactId != sourceArtifactId)
                continue;

            var hasContains =
                relationships.Any(relationship =>
                    relationship.SourceArtifactId == sourceArtifactId &&
                    relationship.TargetArtifactId == artifact.Id &&
                    string.Equals(
                        relationship.RelationshipType,
                        RelationshipTypes.Contains,
                        StringComparison.Ordinal));

            if (!hasContains)
            {
                throw new InvalidOperationException(
                    "Bounded evidence source containment relationship is missing.");
            }

            results.Add(
                new VeteransBoundedEvidenceSelection
                {
                    Artifact = artifact,
                    SourceArtifactId = sourceArtifactId,
                    ClaimIssueId = artifactClaimIssueId,
                    BasisId = artifactBasisId,
                    RequirementIds = requirementIds,
                    SourceStartPage =
                        GetRequiredMetadataInt(
                            artifact,
                            VeteransArtifactMetadataKeys.SourceStartPage),
                    SourceEndPage =
                        GetRequiredMetadataInt(
                            artifact,
                            VeteransArtifactMetadataKeys.SourceEndPage),
                    SourceStartLine =
                        GetRequiredMetadataInt(
                            artifact,
                            VeteransArtifactMetadataKeys.SourceStartLine),
                    SourceEndLine =
                        GetRequiredMetadataInt(
                            artifact,
                            VeteransArtifactMetadataKeys.SourceEndLine),
                    EvidenceDate =
                        GetRequiredMetadataDate(
                            artifact,
                            VeteransArtifactMetadataKeys.EvidenceDate),
                    EvidenceTitle =
                        GetRequiredMetadataText(
                            artifact,
                            VeteransArtifactMetadataKeys.EvidenceTitle)
                });
        }

        return results
            .OrderBy(item => item.EvidenceDate)
            .ThenBy(item => item.SourceStartPage)
            .ThenBy(item => item.SourceStartLine)
            .ThenBy(item => item.Artifact.Id.Value, StringComparer.Ordinal)
            .ToArray();
    }

    private static string GetRequiredMetadataText(
        Artifact artifact,
        string key)
    {
        if (!artifact.Metadata.TryGetValue(key, out var value))
        {
            throw new InvalidOperationException(
                $"Bounded evidence metadata '{key}' is missing.");
        }

        var text =
            value switch
            {
                JsonElement element when element.ValueKind == JsonValueKind.String =>
                    element.GetString(),
                _ => value?.ToString()
            };

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException(
                $"Bounded evidence metadata '{key}' is blank.");
        }

        return text;
    }

    private static int GetRequiredMetadataInt(
        Artifact artifact,
        string key)
    {
        var text = GetRequiredMetadataText(artifact, key);

        if (!int.TryParse(text, out var value) || value <= 0)
        {
            throw new InvalidOperationException(
                $"Bounded evidence metadata '{key}' is invalid.");
        }

        return value;
    }

    private static DateOnly GetRequiredMetadataDate(
        Artifact artifact,
        string key)
    {
        var text = GetRequiredMetadataText(artifact, key);

        if (!DateOnly.TryParseExact(
                text,
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var value))
        {
            throw new InvalidOperationException(
                $"Bounded evidence metadata '{key}' is invalid.");
        }

        return value;
    }

    private static IReadOnlyList<RequirementId>
        GetRequiredRequirementIds(Artifact artifact)
    {
        if (!artifact.Metadata.TryGetValue(
                VeteransArtifactMetadataKeys.RequirementIds,
                out var value))
        {
            throw new InvalidOperationException(
                "Bounded evidence requirement metadata is missing.");
        }

        IEnumerable<string> values =
            value switch
            {
                JsonElement element when element.ValueKind == JsonValueKind.Array =>
                    element
                        .EnumerateArray()
                        .Select(item =>
                            item.ValueKind == JsonValueKind.String
                                ? item.GetString()
                                : null)
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .Select(item => item!),
                IEnumerable<string> strings => strings,
                IEnumerable<object> objects =>
                    objects
                        .Select(item => item?.ToString())
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .Select(item => item!),
                _ => []
            };

        var results =
            values
                .Select(item => item.Trim())
                .Where(item => item.Length != 0)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal)
                .Select(item => new RequirementId(item))
                .ToArray();

        if (results.Length == 0)
        {
            throw new InvalidOperationException(
                "Bounded evidence requirement metadata is empty or invalid.");
        }

        return results;
    }
}
