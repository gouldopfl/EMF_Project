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

        foreach (var progressionEvent in persisted.Where(
                     item => underlyingArtifactIds.Contains(item.SourceArtifactId)))
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
            .ThenBy(item => item.Summary, StringComparer.Ordinal)
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

}
