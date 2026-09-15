using System.Globalization;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models;
using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerPackageSourceClarificationService
{
    private readonly ISourceClarificationRepository _clarifications;

    public VeteransReviewerPackageSourceClarificationService(
        ISourceClarificationRepository clarifications)
    {
        ArgumentNullException.ThrowIfNull(clarifications);
        _clarifications = clarifications;
    }

    public async Task<IReadOnlyList<VeteransReviewerSourceClarification>>
        GetAsync(
            VeteransReviewerPackageDetails details,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(details);

        var package = details.PackageDetails.Package;
        var persisted =
            await _clarifications.GetAsync(
                package.ClaimIssueId,
                cancellationToken);

        if (persisted.Any(item => item.ClaimIssueId != package.ClaimIssueId))
        {
            throw new InvalidOperationException(
                "Reviewer source clarification claim issue mismatch.");
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

        var result = new List<VeteransReviewerSourceClarification>();

        foreach (var clarification in persisted)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var derivedMatches =
                contents
                    .Where(
                        content =>
                            IsDerivedFrom(
                                content,
                                clarification.SourceArtifactId) &&
                            ContainsSourceRange(
                                content.Artifact,
                                clarification.SourceStartPage,
                                clarification.SourceEndPage))
                    .ToArray();

            if (derivedMatches.Length > 1)
            {
                throw new InvalidOperationException(
                    "Source clarification maps to multiple bounded reviewer records.");
            }

            VeteransReviewerArtifactContent? match =
                derivedMatches.SingleOrDefault();

            if (match is null)
            {
                var directMatches =
                    contents
                        .Where(
                            content =>
                                content.Artifact.Id ==
                                clarification.SourceArtifactId)
                        .ToArray();

                if (directMatches.Length > 1)
                {
                    throw new InvalidOperationException(
                        "Source clarification maps to multiple reviewer records.");
                }

                match = directMatches.SingleOrDefault();
            }

            if (match is null)
                continue;

            var sourceName =
                VeteransReviewerDisplayNameResolver.Resolve(
                    "Source Record",
                    match.SourceName,
                    match.Artifact.Name);

            result.Add(
                new VeteransReviewerSourceClarification
                {
                    ReviewerArtifactId = match.Artifact.Id,
                    SourceLocator =
                        $"{sourceName} — {clarification.RecordTitle.Trim()} — " +
                        clarification.EvidenceDate.ToString(
                            "MMMM d, yyyy",
                            CultureInfo.InvariantCulture),
                    OriginalText = clarification.OriginalText.Trim(),
                    Clarification = clarification.Clarification.Trim(),
                    ReviewerMatchText = clarification.ReviewerMatchText?.Trim(),
                    ReviewerReplacementText =
                        clarification.ReviewerReplacementText?.Trim()
                });
        }

        return result
            .OrderBy(item => item.SourceLocator, StringComparer.Ordinal)
            .ThenBy(item => item.OriginalText, StringComparer.Ordinal)
            .ToArray();
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
        int clarificationStartPage,
        int clarificationEndPage)
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
            throw new InvalidDataException(
                "Reviewer evidence source page range is invalid.");

        return clarificationStartPage >= sourceStartPage &&
               clarificationEndPage <= sourceEndPage;
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
            throw new InvalidDataException(
                "Reviewer evidence source page must be positive.");

        return true;
    }
}
