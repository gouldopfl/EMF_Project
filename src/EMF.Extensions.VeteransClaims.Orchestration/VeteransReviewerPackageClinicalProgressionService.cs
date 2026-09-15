using System.Globalization;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Clinical;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerPackageClinicalProgressionService
{
    private readonly IClinicalProgressionRepository _progression;

    public VeteransReviewerPackageClinicalProgressionService(
        IClinicalProgressionRepository progression)
    {
        ArgumentNullException.ThrowIfNull(progression);
        _progression = progression;
    }

    public async Task<IReadOnlyList<VeteransReviewerClinicalProgressionEvent>>
        GetAsync(
            VeteransReviewerPackageDetails details,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(details);

        var package = details.PackageDetails.Package;
        var persisted =
            await _progression.GetAsync(
                package.ClaimIssueId,
                cancellationToken);

        if (persisted.Any(item => item.ClaimIssueId != package.ClaimIssueId))
        {
            throw new InvalidOperationException(
                "Reviewer clinical progression claim issue mismatch.");
        }

        if (persisted.Count == 0)
            return [];

        var underlyingArtifactIds =
            details.PackageDetails.Artifacts
                .Where(
                    item => string.Equals(
                        item.ContentRole,
                        EvidencePackageContentRoles.UnderlyingEvidence,
                        StringComparison.Ordinal))
                .Select(item => item.ArtifactId)
                .ToHashSet();

        var contents =
            details.ArtifactContents
                .Where(content => underlyingArtifactIds.Contains(content.Artifact.Id))
                .ToArray();

        var result = new List<VeteransReviewerClinicalProgressionEvent>();

        foreach (var progressionEvent in persisted)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Validate(progressionEvent);

            var directMatches =
                contents
                    .Where(
                        content =>
                            content.Artifact.Id == progressionEvent.SourceArtifactId)
                    .ToArray();

            if (directMatches.Length > 1)
            {
                throw new InvalidOperationException(
                    "Clinical progression event maps to multiple reviewer records.");
            }

            VeteransReviewerArtifactContent? match =
                directMatches.SingleOrDefault();

            if (match is null &&
                progressionEvent.SourceStartPage is int startPage &&
                progressionEvent.SourceEndPage is int endPage)
            {
                var derivedMatches =
                    contents
                        .Where(
                            content =>
                                IsDerivedFrom(
                                    content,
                                    progressionEvent.SourceArtifactId) &&
                                ContainsSourceRange(
                                    content.Artifact,
                                    startPage,
                                    endPage))
                        .ToArray();

                if (derivedMatches.Length > 1)
                {
                    throw new InvalidOperationException(
                        "Clinical progression event maps to multiple bounded reviewer records.");
                }

                match = derivedMatches.SingleOrDefault();
            }

            if (match is null)
                continue;

            var sourceName =
                VeteransReviewerDisplayNameResolver.Resolve(
                    "Source Record",
                    match.SourceName,
                    match.Artifact.Name);

            result.Add(
                new VeteransReviewerClinicalProgressionEvent
                {
                    ReviewerArtifactId = match.Artifact.Id,
                    EventDate = progressionEvent.EventDate,
                    EventType = progressionEvent.EventType.Trim(),
                    SourceLocator =
                        $"{sourceName} — {progressionEvent.RecordTitle.Trim()} — " +
                        progressionEvent.EventDate.ToString(
                            "MMMM d, yyyy",
                            CultureInfo.InvariantCulture),
                    Summary = progressionEvent.Summary.Trim()
                });
        }

        return result
            .OrderBy(item => item.EventDate)
            .ThenBy(item => item.SourceLocator, StringComparer.Ordinal)
            .ThenBy(item => item.EventType, StringComparer.Ordinal)
            .ToArray();
    }

    private static void Validate(ClinicalProgressionEvent progressionEvent)
    {
        if (!ClinicalProgressionEventTypes.IsSupported(
                progressionEvent.EventType.Trim()))
        {
            throw new InvalidOperationException(
                "Reviewer clinical progression contains an unsupported event type.");
        }

        if (string.IsNullOrWhiteSpace(progressionEvent.RecordTitle) ||
            string.IsNullOrWhiteSpace(progressionEvent.Summary))
        {
            throw new InvalidOperationException(
                "Reviewer clinical progression event is incomplete.");
        }

        var hasStart = progressionEvent.SourceStartPage.HasValue;
        var hasEnd = progressionEvent.SourceEndPage.HasValue;

        if (hasStart != hasEnd)
        {
            throw new InvalidOperationException(
                "Reviewer clinical progression source page range is incomplete.");
        }

        if (progressionEvent.SourceStartPage is int startPage &&
            progressionEvent.SourceEndPage is int endPage &&
            (startPage <= 0 || endPage < startPage))
        {
            throw new InvalidOperationException(
                "Reviewer clinical progression source page range is invalid.");
        }
    }

    private static bool IsDerivedFrom(
        VeteransReviewerArtifactContent content,
        ArtifactId sourceArtifactId) =>
        content.Relationships.Any(
            relationship =>
                relationship.SourceArtifactId == content.Artifact.Id &&
                relationship.TargetArtifactId == sourceArtifactId &&
                string.Equals(
                    relationship.RelationshipType,
                    RelationshipTypes.DerivedFrom,
                    StringComparison.Ordinal));

    private static bool ContainsSourceRange(
        Artifact artifact,
        int eventStartPage,
        int eventEndPage)
    {
        if (!TryGetPositivePage(
                artifact,
                VeteransArtifactMetadataKeys.SourceStartPage,
                out var sourceStartPage) ||
            !TryGetPositivePage(
                artifact,
                VeteransArtifactMetadataKeys.SourceEndPage,
                out var sourceEndPage))
        {
            return false;
        }

        if (sourceEndPage < sourceStartPage)
        {
            throw new InvalidDataException(
                "Reviewer evidence source page range is invalid.");
        }

        return eventStartPage >= sourceStartPage &&
               eventEndPage <= sourceEndPage;
    }

    private static bool TryGetPositivePage(
        Artifact artifact,
        string key,
        out int page)
    {
        page = 0;

        if (!artifact.Metadata.TryGetValue(key, out var value) ||
            value is null ||
            !int.TryParse(
                value.ToString(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out page))
        {
            return false;
        }

        if (page <= 0)
        {
            throw new InvalidDataException(
                "Reviewer evidence source page must be positive.");
        }

        return true;
    }
}
