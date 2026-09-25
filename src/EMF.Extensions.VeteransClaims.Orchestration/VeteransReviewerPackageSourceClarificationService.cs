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

            var scope = new VeteransReviewerPackageEvidenceScope(details);
            if (!scope.Contains(clarification.SourceArtifactId,
                    clarification.SourceStartPage, clarification.SourceEndPage))
                continue;
            var match = contents.SingleOrDefault(content => content.Artifact.Id == clarification.SourceArtifactId)
                ?? throw new InvalidDataException("Reviewer clarification member content is missing.");

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
            .ThenBy(item => item.Clarification, StringComparer.Ordinal)
            .ThenBy(item => item.ReviewerMatchText, StringComparer.Ordinal)
            .ThenBy(item => item.ReviewerReplacementText, StringComparer.Ordinal)
            .ToArray();
    }

}
