using System.Globalization;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models;
using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Orchestration;

/// <summary>Direct persisted membership and selected source pages for factual projections.</summary>
internal sealed class VeteransReviewerPackageEvidenceScope(VeteransReviewerPackageDetails details)
{
    public IReadOnlySet<ArtifactId> ArtifactIds { get; } = details.PackageDetails.Artifacts
        .Where(row => row.ContentRole == EvidencePackageContentRoles.UnderlyingEvidence)
        .Select(row => row.ArtifactId).ToHashSet();

    public bool Contains(ArtifactId sourceId, int start, int end)
    {
        if (!ArtifactIds.Contains(sourceId)) return false;
        if (start <= 0 || end < start)
            throw new InvalidDataException("Reviewer factual source range is invalid.");
        var row = details.PackageDetails.Artifacts.Single(row => row.ArtifactId == sourceId);
        if (row.EvidencePackageId != details.PackageDetails.Package.Id)
            throw new InvalidDataException("Reviewer factual source package identity mismatch.");
        var artifact = details.Artifacts.SingleOrDefault(item => item.Id == sourceId)
            ?? throw new InvalidDataException("Reviewer factual source artifact is missing.");
        var content = details.ArtifactContents.SingleOrDefault(item => item.Artifact.Id == sourceId)
            ?? throw new InvalidDataException("Reviewer factual source content is missing.");
        var hasStart = artifact.Metadata.TryGetValue(VeteransArtifactMetadataKeys.SourceStartPage, out var sourceStart);
        var hasEnd = artifact.Metadata.TryGetValue(VeteransArtifactMetadataKeys.SourceEndPage, out var sourceEnd);
        if (hasStart || hasEnd)
        {
            if (!hasStart || !hasEnd || !int.TryParse(sourceStart?.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var first) ||
                !int.TryParse(sourceEnd?.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var last) || first <= 0 || last < first)
                throw new InvalidDataException("Reviewer factual artifact lineage range is invalid.");
            if (start < first || end > last) return false;
        }
        if (row.ReviewerPageSelection is null) return true;
        var pages = VeteransReviewerPageSelector.Select(content.PrintablePages, row.ReviewerPageSelection)
            .Select(page => page.PageNumber).ToHashSet();
        return (long)end - start + 1 <= pages.Count &&
            Enumerable.Range(start, end - start + 1).All(pages.Contains);
    }
}
