using System.Text.RegularExpressions;

namespace EMF.Extensions.VeteransClaims.Orchestration;

internal static class VeteransReviewerLayTextSupplement
{
    // DOCX text boxes can contain typed text which their fixed-height printable
    // rendition clips. Use only independently stored text and exact comparison;
    // never alter the image or reconstruct a signature from extraction output.
    public static IReadOnlyList<string>? Get(VeteransReviewerArtifactContent content)
    {
        if (content.Appendix != VeteransReviewerPackageAppendix.LayEvidence ||
            !content.Artifact.Name.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) ||
            content.PrintablePages.Count == 0 || content.PrintablePages.Any(p =>
                p.ContentType != "image/png" || p.TextGeometry is not { Glyphs.Count: > 0 })) return null;

        // A complete typed certification provides an explicit stop before signer
        // names, signature images, and potentially OCR-derived handwritten dates.
        var certification = Regex.Match(content.Text, @"\bI certify\b[^.!?]{20,}[.!?]",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!certification.Success) return null;
        var body = content.Text[..(certification.Index + certification.Length)];
        var paragraphs = Regex.Split(body, @"(?<=[.!?])\s*(?=[A-Z])")
            .Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
        static string Compact(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
        var printable = Compact(string.Join(" ", content.PrintablePages
            .SelectMany(VeteransReviewerNativeProse.Lines).Select(line => line.Text)));
        var missing = paragraphs.Where(p => !printable.Contains(Compact(p), StringComparison.Ordinal)).ToArray();
        // Keep only incomplete/missing sentences, retaining a whole sentence
        // where its visible prefix is needed to make a clipped ending intelligible.
        return missing.Any(p => p.Length >= 80) ? missing : null;
    }
}
