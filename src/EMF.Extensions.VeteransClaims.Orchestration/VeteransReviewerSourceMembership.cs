using EMF.Core.Models;

namespace EMF.Extensions.VeteransClaims.Orchestration;

// Presentation membership only. The package ledger, source selections and all
// source artifacts remain intact, including projections subsumed by another.
internal static class VeteransReviewerSourceMembership
{
    public static IReadOnlyList<VeteransReviewerArtifactContent> Select(
        VeteransReviewerPackageDetails details, IReadOnlyList<VeteransReviewerArtifactContent> contents)
    {
        IReadOnlyList<PrintableArtifactPage> Pages(VeteransReviewerArtifactContent content)
        {
            var selection = details.PackageDetails.Artifacts.First(a => a.ArtifactId == content.Artifact.Id)
                .ReviewerPageSelection;
            return selection is null ? content.PrintablePages : VeteransReviewerPageSelector.Select(content.PrintablePages, selection);
        }
        var pages = contents.ToDictionary(c => c.Artifact.Id, Pages);
        return contents.Where(candidate => !contents.Any(other =>
            other.Artifact.Id != candidate.Artifact.Id &&
            candidate.Artifact.ArtifactType == "veterans-clinical-note" &&
            other.Artifact.ArtifactType == candidate.Artifact.ArtifactType &&
            candidate.PrintableSourceArtifactId is not null &&
            candidate.PrintableSourceArtifactId == other.PrintableSourceArtifactId &&
            Covers(pages[other.Artifact.Id], pages[candidate.Artifact.Id]) &&
            (!Covers(pages[candidate.Artifact.Id], pages[other.Artifact.Id]) ||
             string.CompareOrdinal(other.Artifact.Id.Value, candidate.Artifact.Id.Value) < 0))).Select(ForPresentation).ToArray();
    }

    private static VeteransReviewerArtifactContent ForPresentation(VeteransReviewerArtifactContent content)
    {
        var appendix = Appendix(content);
        if (appendix == content.Appendix || appendix is null) return content;
        return new()
        {
            Artifact = content.Artifact, Text = content.Text, Appendix = appendix,
            MedicalLiteratureReviewerText = content.MedicalLiteratureReviewerText,
            PrintablePages = content.PrintablePages, ReviewerPageSelection = content.ReviewerPageSelection,
            PrintableSourceArtifactId = content.PrintableSourceArtifactId,
            IsExtractedTextFallback = content.IsExtractedTextFallback, SourceName = content.SourceName,
            Provenance = content.Provenance, Relationships = content.Relationships,
            ReviewedMedicalLiteratureClassifications = content.ReviewedMedicalLiteratureClassifications
        };
    }

    private static bool Covers(IReadOnlyList<PrintableArtifactPage> outer, IReadOnlyList<PrintableArtifactPage> inner) =>
        inner.Count > 0 && inner.All(page => outer.Any(other =>
            other.PageNumber == page.PageNumber && other.ContentType == page.ContentType &&
            (other.Content.Span.SequenceEqual(page.Content.Span) ||
             (other.TextGeometry is { ContainsGraphics: false, Glyphs.Count: > 0 } a &&
              page.TextGeometry is { ContainsGraphics: false, Glyphs.Count: > 0 } b &&
              a.Width == b.Width && a.Height == b.Height &&
              other.SuggestedClockwiseRotation == page.SuggestedClockwiseRotation &&
              b.Glyphs.All(a.Glyphs.ToHashSet().Contains)))));

    public static string? Appendix(VeteransReviewerArtifactContent content)
    {
        if (content.Appendix != VeteransReviewerPackageAppendix.LayEvidence) return content.Appendix;
        // A patient's message inside a clinical note is still medical-record evidence.
        if (content.Artifact.ArtifactType == "veterans-clinical-note")
            return VeteransReviewerPackageAppendix.MedicalEvidence;
        // Separate clarifications remain outside Appendix D,
        // without representing them as one of the requested lay statements.
        if (content.Artifact.Name.Contains("Lay Clarification", StringComparison.OrdinalIgnoreCase)) return null;
        return content.Appendix;
    }
}
