using EMF.Common;
using System.Globalization;
using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models;

namespace EMF.Extensions.VeteransClaims.Orchestration;

internal sealed record VeteransReviewerPrintableSource(
    IReadOnlyList<PrintableArtifactPage> Pages,
    ArtifactId? SourceArtifactId = null,
    bool IsExtractedTextFallback = false);

/// <summary>Resolves a derived text excerpt to its explicitly bounded native source.</summary>
internal sealed class VeteransReviewerPrintableSourceResolver(
    IEvidenceRepository evidence, IArtifactPrintRenderer renderer)
{
    public async Task<VeteransReviewerPrintableSource> ResolveAsync(
        Artifact artifact, CancellationToken cancellationToken, string? excerptText = null)
    {
        using var performanceTiming = EmfPerformanceTiming.Measure(EmfPerformancePhase.SourcePageRender);
        // A native artifact remains authoritative in its own right. Only text
        // derivatives need their immediate parent's page-coordinate mapping.
        var extension = Extension(artifact);
        var clinicalNote = Text(artifact, ArtifactMetadataKeys.SourceType) == "veterans-clinical-note" ||
            artifact.ArtifactType == "veterans-clinical-note";
        var declaresBoundary = new[] { VeteransArtifactMetadataKeys.SourceStartPage,
            VeteransArtifactMetadataKeys.SourceEndPage, VeteransArtifactMetadataKeys.SourceStartText,
            VeteransArtifactMetadataKeys.SourceEndText }.Any(artifact.Metadata.ContainsKey);
        if ((clinicalNote || declaresBoundary) &&
            extension is not (".txt" or ".md" or ".markdown" or ".html" or ".htm"))
            throw new InvalidDataException("Bounded reviewer evidence has a contradictory or unsupported artifact format.");
        if (extension is ".pdf" or ".docx" ||
            (extension is not (".txt" or ".md" or ".markdown" or ".html" or ".htm") &&
             Text(artifact, ArtifactMetadataKeys.ContentType) != "text/plain"))
            return new(await renderer.RenderAsync(artifact.Id, cancellationToken));

        var relationships = await evidence.GetRelationshipsAsync(artifact.Id, cancellationToken);
        if (relationships.Any(r => r.SourceArtifactId != artifact.Id && r.TargetArtifactId != artifact.Id))
            throw new InvalidOperationException("Printable source lookup returned an unrelated relationship.");
        var parents = relationships.Where(r => r.SourceArtifactId == artifact.Id &&
            r.RelationshipType == RelationshipTypes.DerivedFrom).ToArray();
        if (parents.Length > 1)
            throw new InvalidOperationException("Reviewer evidence has ambiguous source provenance.");
        if (parents.Length == 0 && (clinicalNote || declaresBoundary))
            throw new InvalidDataException("Bounded reviewer evidence requires explicit source provenance.");
        if (parents.Length == 0)
            return new(await renderer.RenderAsync(artifact.Id, cancellationToken));

        var parentId = parents[0].TargetArtifactId;
        var parent = await evidence.GetArtifactAsync(parentId, cancellationToken)
            ?? throw new InvalidOperationException($"Reviewer evidence source artifact '{parentId.Value}' was not found.");
        if (parent.Id != parentId || parent.Id == artifact.Id)
            throw new InvalidOperationException("Printable source artifact identity mismatch or self-reference.");

        var startText = Text(artifact, VeteransArtifactMetadataKeys.SourceStartPage);
        var endText = Text(artifact, VeteransArtifactMetadataKeys.SourceEndPage);
        if (Extension(parent) is not (".pdf" or ".docx"))
        {
            if (clinicalNote || declaresBoundary)
                throw new InvalidDataException("Bounded reviewer evidence requires a supported native source.");
            return new(await renderer.RenderAsync(artifact.Id, cancellationToken), IsExtractedTextFallback: true);
        }

        if (!int.TryParse(startText, NumberStyles.None, CultureInfo.InvariantCulture, out var start) ||
            !int.TryParse(endText, NumberStyles.None, CultureInfo.InvariantCulture, out var end) ||
            start <= 0 || end < start)
            throw new InvalidDataException("Derived evidence requires a valid inclusive source-page range.");

        var pages = renderer is IArtifactPageRangePrintRenderer ranged
            ? await ranged.RenderRangeAsync(parent.Id, start, end, cancellationToken)
            : (await renderer.RenderAsync(parent.Id, cancellationToken))
                .Where(page => page.PageNumber >= start && page.PageNumber <= end).ToArray();
        // A declared native source that fails to render must not silently fall
        // back to the very extraction whose geometry may be damaged.
        if (pages.Count == 0)
            throw new InvalidOperationException("The selected authoritative source range has no printable pages.");
        var previous = start - 1;
        foreach (var page in pages)
        {
            if ((long)page.PageNumber != (long)previous + 1 || page.PageNumber > end ||
                !string.Equals(page.ContentType, "image/png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Native source rendering returned invalid page order, range, or format.");
            previous = page.PageNumber;
        }
        if (previous != end)
            throw new InvalidDataException("Native source rendering omitted declared source pages.");
        var startAnchor = Text(artifact, VeteransArtifactMetadataKeys.SourceStartText);
        var endAnchor = Text(artifact, VeteransArtifactMetadataKeys.SourceEndText);
        if (clinicalNote || startAnchor is not null || endAnchor is not null)
        {
            if (startAnchor is null && endAnchor is null && !string.IsNullOrWhiteSpace(excerptText))
                pages = VeteransReviewerNativeExcerpt.Match(pages, excerptText);
            else if (string.IsNullOrWhiteSpace(startAnchor) || string.IsNullOrWhiteSpace(endAnchor))
                throw new InvalidDataException(
                    $"Reviewer evidence '{artifact.Id.Value}' requires reviewed native text boundaries; " +
                    "a Blue Button page range alone can include adjacent records.");
            else pages = VeteransReviewerNativeExcerpt.Restrict(pages, start, end, startAnchor, endAnchor);
        }
        return new(pages, parent.Id);
    }

    private static string Extension(Artifact artifact) =>
        (Text(artifact, ArtifactMetadataKeys.FileExtension) ?? Path.GetExtension(artifact.Name)).ToLowerInvariant();

    private static string? Text(Artifact artifact, string key) =>
        artifact.Metadata.TryGetValue(key, out var value) ? value?.ToString() : null;
}
