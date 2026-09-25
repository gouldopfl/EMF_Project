using System.Text;
using EMF.Core.Models;
using SkiaSharp;

namespace EMF.Extensions.VeteransClaims.Orchestration;

/// <summary>
/// Applies reviewed text boundaries to native page pixels before presentation.
/// It never searches outside declared source pages or infers clinical relevance.
/// </summary>
internal static class VeteransReviewerNativeExcerpt
{
    // For ordinary derivations, the preserved excerpt itself supplies the exact
    // boundaries. Reviewed anchors support audited legacy lineage corrections.
    public static IReadOnlyList<PrintableArtifactPage> Match(
        IReadOnlyList<PrintableArtifactPage> pages, string excerptText)
    {
        var rows = new List<(int Page, string Text)>();
        foreach (var page in pages)
        {
            var geometry = page.TextGeometry ??
                throw new InvalidDataException("Cannot verify a native excerpt without source text geometry.");
            rows.AddRange(geometry.Glyphs.Where(g => !string.IsNullOrWhiteSpace(g.Text))
                .GroupBy(g => Math.Round(g.Baseline, 1)).OrderBy(g => g.Key)
                .Select(g => string.Concat(g.OrderBy(l => l.X).Select(l => l.Text)))
                .Where(t => !IsPageFurniture(t)).Select(t => (page.PageNumber, t)));
        }
        var expected = string.Join("\n", excerptText.Replace("\r", "").Split('\n')
            .Where(t => !IsPageFurniture(t)));
        var (first, last) = Find(rows.Select(r => Compact(r.Text)).ToArray(), expected);
        var start = rows[first].Page;
        var end = rows[last].Page;
        var selected = rows.Skip(first).Take(last - first + 1).ToArray();
        return Restrict(pages.Where(p => p.PageNumber >= start && p.PageNumber <= end).ToArray(), start, end,
            string.Join("\n", selected.Where(r => r.Page == start).Select(r => r.Text)),
            string.Join("\n", selected.Where(r => r.Page == end).Select(r => r.Text)));
    }

    private static bool IsPageFurniture(string text)
    {
        var compact = Compact(text);
        return compact.StartsWith("ReportgeneratedbyMyHealtheVet", StringComparison.Ordinal) ||
            compact.Contains("Dateofbirth:", StringComparison.Ordinal);
    }

    public static IReadOnlyList<PrintableArtifactPage> Restrict(
        IReadOnlyList<PrintableArtifactPage> pages, int startPage, int endPage,
        string startText, string endText)
    {
        if (!pages.Any(p => p.PageNumber == startPage) || !pages.Any(p => p.PageNumber == endPage))
            throw new InvalidDataException("Native excerpt boundary pages are missing.");

        return pages.Select(page =>
        {
            if (page.PageNumber < startPage || page.PageNumber > endPage)
                throw new InvalidDataException("Native excerpt contains an out-of-range page.");
            if (page.PageNumber != startPage && page.PageNumber != endPage) return page;

            var geometry = page.TextGeometry;
            if (geometry is null || geometry.ContainsGraphics || page.SuggestedClockwiseRotation != 0 ||
                geometry.Width <= 0 || geometry.Height <= 0)
                throw new InvalidDataException("Native excerpt requires unambiguous boundary-page text geometry.");

            var rows = geometry.Glyphs.Where(g => !string.IsNullOrWhiteSpace(g.Text))
                .GroupBy(g => Math.Round(g.Baseline, 1)).OrderBy(g => g.Key)
                .Select(g => g.OrderBy(letter => letter.X).ToArray()).ToArray();
            var rowText = rows.Select(r => Compact(string.Concat(r.Select(g => g.Text)))).ToArray();
            var first = page.PageNumber == startPage ? Find(rowText, startText).First : 0;
            var last = page.PageNumber == endPage ? Find(rowText, endText).Last : rows.Length - 1;
            if (first > last)
                throw new InvalidDataException("Native excerpt text boundaries are reversed.");

            using var bitmap = SKBitmap.Decode(page.Content.Span)
                ?? throw new InvalidDataException("Native excerpt image cannot be decoded.");
            var scale = bitmap.Height / geometry.Height;
            var top = page.PageNumber == startPage ? Math.Max(0, (int)Math.Floor(rows[first].Min(g => g.Top) * scale) - 1) : 0;
            var bottom = page.PageNumber == endPage ? Math.Min(bitmap.Height, (int)Math.Ceiling(rows[last].Max(g => g.Bottom) * scale) + 1) : bitmap.Height;
            // Do not erase ink from a neighboring row that overlaps the reviewed
            // boundary. Such geometry needs an explicit source review instead.
            if ((first > 0 && rows[first - 1].Max(g => g.Bottom) * scale >= top) ||
                (last + 1 < rows.Length && rows[last + 1].Min(g => g.Top) * scale <= bottom))
                throw new InvalidDataException("Native excerpt boundary overlaps an adjacent source row.");

            var pixels = bitmap.Pixels;
            Array.Fill(pixels, SKColors.White, 0, top * bitmap.Width);
            Array.Fill(pixels, SKColors.White, bottom * bitmap.Width, (bitmap.Height - bottom) * bitmap.Width);
            bitmap.Pixels = pixels;
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            return new PrintableArtifactPage
            {
                PageNumber = page.PageNumber, ContentType = page.ContentType,
                Content = encoded.ToArray(), SuggestedClockwiseRotation = page.SuggestedClockwiseRotation,
                TextGeometry = geometry with { Glyphs = rows.Skip(first).Take(last - first + 1).SelectMany(r => r).ToArray() }
            };
        }).ToArray();
    }

    private static (int First, int Last) Find(string[] rows, string anchor)
    {
        var expected = Compact(anchor);
        if (expected.Length == 0) throw new InvalidDataException("Native excerpt text boundary is empty.");
        var offsets = new int[rows.Length + 1];
        for (var i = 0; i < rows.Length; i++) offsets[i + 1] = checked(offsets[i] + rows[i].Length);
        var source = string.Concat(rows);
        var offset = source.IndexOf(expected, StringComparison.Ordinal);
        if (offset < 0 || source.IndexOf(expected, offset + 1, StringComparison.Ordinal) >= 0)
            throw new InvalidDataException("Native excerpt text boundary is missing or ambiguous in its declared source page.");
        var first = Array.IndexOf(offsets, offset);
        var afterLast = Array.IndexOf(offsets, offset + expected.Length);
        if (first < 0 || afterLast <= first)
            throw new InvalidDataException("Native excerpt boundary does not align with complete source rows.");
        return (first, afterLast - 1);
    }

    private static string Compact(string text) =>
        string.Concat(text.Normalize(NormalizationForm.FormKC).Where(c => !char.IsWhiteSpace(c)));
}
