using EMF.Common;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using EMF.Core.Models;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using SkiaSharp;

namespace EMF.Extensions.VeteransClaims.Orchestration;

/// <summary>Deterministic supplements; never changes the preserved source-page view.</summary>
internal sealed record VeteransReviewerSourceEnlargement(
    string Kind, string DetectionMethod, ReadOnlyMemory<byte> Content, SKRectI Bounds,
    long Cx, long Cy, double Magnification, string Confidence, string? RejectionReason)
{
    public bool Landscape { get; init; }
    public bool Selected => RejectionReason is null;
}

internal static class VeteransReviewerSourceEnlargements
{
    internal const double MinimumMagnification = 1.35;
    internal const double CaptionProximityPoints = 24;
    internal const double RasterSeparationPoints = 12;
    internal const double MinimumWidthPoints = 60;
    internal const double MinimumHeightPoints = 35;
    internal const double SafePaddingPoints = 6;
    internal const double RuleCoverage = .8;
    internal const int MinimumHorizontalRules = 3;
    internal const int MinimumVerticalRules = 2;
    internal const int MinimumPopulatedCells = 4;
    internal const double MaximumRegionAreaFraction = .75;
    internal const double MinimumCaptionOverlap = .5;
    internal const int MinimumNonTextInkPixels = 100;
    internal const double MinimumGraphicInkFraction = .2;
    internal const string DetectionVersion = "caption-grid-v2";
    internal const string AuditNamespace = "urn:emf:reviewer:figure-table-regions:v1";

    // Reserve one compact label line and the inline baseline inside the same
    // margins as native source pages. Ties favor portrait for deterministic output.
    internal static (long Width, long Height) ContentBox(bool landscape) => (
        ((landscape ? 15840L : 12240L) - 2 * VeteransReviewerEvidenceSections.SourceSideMargin) * 635,
        ((landscape ? 12240L : 15840L) - VeteransReviewerEvidenceSections.SourceTopMargin -
            VeteransReviewerEvidenceSections.SourceBottomMargin - 280) * 635);

    internal static (double Scale, bool Landscape) BestFit(SKRectI region)
    {
        var portraitBox = ContentBox(false);
        var portrait = Math.Min(portraitBox.Width / (double)region.Width, portraitBox.Height / (double)region.Height);
        var box = ContentBox(true);
        var landscape = Math.Min(box.Width / (double)region.Width, box.Height / (double)region.Height);
        return landscape > portrait ? (landscape, true) : (portrait, false);
    }

    public static void RecordAudit(MainDocumentPart mainPart, string sourceArtifactId,
        PrintableArtifactPage page, ReadOnlyMemory<byte> orientedContent,
        IReadOnlyList<VeteransReviewerSourceEnlargement> regions)
    {
        using var bitmap = SKBitmap.Decode(orientedContent.Span)!;
        var records = regions.Select(region => new ReviewerFigureTableEnlargement(
            sourceArtifactId, page.PageNumber, bitmap.Width, bitmap.Height, page.SuggestedClockwiseRotation,
            region.Bounds.Left, region.Bounds.Top, region.Bounds.Width, region.Bounds.Height,
            "oriented-raster-pixels/top-left/half-open", bitmap.Width > bitmap.Height ? "Landscape" : "Portrait",
            region.Selected ? "Selected" : "Detected", region.RejectionReason, region.Kind, DetectionVersion,
            region.DetectionMethod, region.Confidence, region.Magnification, region.Cx, region.Cy,
            Convert.ToHexString(SHA256.HashData(orientedContent.Span)), "page-privacy-v1/source-orientation-v1",
            VeteransReviewerPackageRendererIdentity.Build, region.Selected
                ? Convert.ToHexString(SHA256.HashData(region.Content.Span)) : null)).ToArray();
        foreach (var record in records) record.Validate();
        var part = mainPart.AddCustomXmlPart(CustomXmlPartType.CustomXml);
        using var stream = part.GetStream(FileMode.Create);
        new XDocument(new XElement(XName.Get("regions", AuditNamespace),
            JsonSerializer.Serialize(records))).Save(stream);
    }

    internal static IReadOnlyList<ReviewerFigureTableEnlargement> ReadFrozenAudit(byte[] docx)
    {
        using var document = WordprocessingDocument.Open(new MemoryStream(docx), false);
        var result = new List<ReviewerFigureTableEnlargement>();
        var images = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var relationshipHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var image in document.MainDocumentPart!.ImageParts)
        {
            using var stream = image.GetStream();
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            var content = bytes.ToArray();
            var hash = Convert.ToHexString(SHA256.HashData(content));
            images.TryAdd(hash, content);
            relationshipHashes.Add(document.MainDocumentPart.GetIdOfPart(image), hash);
        }
        foreach (var part in document.MainDocumentPart!.CustomXmlParts)
        {
            using var stream = part.GetStream();
            var root = XDocument.Load(stream).Root;
            if (root?.Name != XName.Get("regions", AuditNamespace)) continue;
            var records = JsonSerializer.Deserialize<ReviewerFigureTableEnlargement[]>(root.Value)
                ?? throw new InvalidDataException("Missing figure/table selection audit.");
            foreach (var record in records)
            {
                record.Validate();
                if (!images.TryGetValue(record.SourceImageSha256, out var sourceBytes))
                    throw new InvalidDataException("Enlargement audit is bound to a different source raster.");
                using var source = SKBitmap.Decode(sourceBytes);
                if (source is null || source.Width != record.SourceWidth || source.Height != record.SourceHeight)
                    throw new InvalidDataException("Enlargement audit source dimensions do not match its raster.");
                if (record.State != "Detected")
                {
                    if (!images.TryGetValue(record.CropImageSha256!, out var cropBytes))
                        throw new InvalidDataException("Selected enlargement raster is missing from the document.");
                    using var crop = SKBitmap.Decode(cropBytes);
                    if (crop is null || crop.Width != record.Width || crop.Height != record.Height)
                        throw new InvalidDataException("Selected enlargement does not match its frozen crop dimensions.");
                    if (!document.MainDocumentPart.Document!.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Inline>()
                        .Any(inline => inline.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().Any(blip =>
                            blip.Embed?.Value is { } id && relationshipHashes.TryGetValue(id, out var hash) &&
                            hash == record.CropImageSha256) && inline.Extent?.Cx?.Value == record.DisplayWidthEmus &&
                            inline.Extent.Cy?.Value == record.DisplayHeightEmus))
                        throw new InvalidDataException("Selected enlargement display scale does not match its frozen drawing.");
                    var sourcePixels = source.Pixels;
                    var cropPixels = crop.Pixels;
                    for (var y = 0; y < record.Height; y++)
                    for (var x = 0; x < record.Width; x++)
                        if (sourcePixels[(record.Top + y) * source.Width + record.Left + x] != cropPixels[y * crop.Width + x])
                            throw new InvalidDataException("Selected enlargement pixels do not match the recorded source bounds.");
                }
                result.Add(record.State == "Selected" ? record with { State = "Frozen" } : record);
            }
        }
        return result;
    }

    public static IReadOnlyList<VeteransReviewerSourceEnlargement> Find(
        PrintableArtifactPage page, ReadOnlyMemory<byte> orientedContent,
        long sourceCx, long sourceCy)
    {
        using var performanceTiming = EmfPerformanceTiming.Measure(EmfPerformancePhase.EnlargementDetection);
        using var bitmap = SKBitmap.Decode(orientedContent.Span)
            ?? throw new InvalidDataException("Source enlargement requires a decodable PNG page.");
        var width = bitmap.Width;
        var height = bitmap.Height;
        // Native coordinates are optional. Uncaptioned figures are never guessed
        // from photos or blocks of prose; a clearly ruled table can stand alone.
        var geometry = page.SuggestedClockwiseRotation == 0 &&
            page.TextGeometry is { Width: > 0, Height: > 0 } g ? g : null;
        var pixels = bitmap.Pixels;
        var pointsPerPixel = geometry is null ? 612d / Math.Min(width, height) : geometry.Width / width;
        var gap = Math.Max(2, (int)Math.Ceiling(RasterSeparationPoints / pointsPerPixel));
        var rowInk = new bool[height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            rowInk[y] |= IsInk(pixels[y * width + x]);
        var candidates = new List<SKRectI>();
        foreach (var (top, bottom) in Bands(rowInk, gap))
        {
            var columnInk = new bool[width];
            for (var y = top; y < bottom; y++)
            for (var x = 0; x < width; x++)
                columnInk[x] |= IsInk(pixels[y * width + x]);
            foreach (var (left, right) in Bands(columnInk, gap))
            {
                var bounds = new SKRectI(left, top, right, bottom);
                if (bounds.Width * pointsPerPixel >= MinimumWidthPoints && bounds.Height * pointsPerPixel >= MinimumHeightPoints &&
                    (double)bounds.Width * bounds.Height < width * (double)height * MaximumRegionAreaFraction)
                    candidates.Add(bounds);
            }
        }
        var captions = geometry is null ? new List<Caption>() : Captions(geometry, width, height);
        var results = new List<VeteransReviewerSourceEnlargement>();
        foreach (var candidate in candidates.OrderBy(b => b.Top).ThenBy(b => b.Left))
        {
            var nearby = captions.Where(c => Near(candidate, c.Bounds, pointsPerPixel)).ToArray();
            // A caption shared by neighboring blocks, or multiple distinct
            // captions in one block, cannot establish an unambiguous region.
            var caption = nearby.Length == 1 && candidates.Count(c => Near(c, nearby[0].Bounds, pointsPerPixel)) == 1
                ? nearby[0] : null;
            string? kind = null;
            if (caption is not null)
            {
                if (caption.Kind == "Table" || HasNonTextGraphics(candidate, geometry!, pixels, width))
                    kind = caption.Kind;
            }
            else if (nearby.Length == 0 && IsRuledTable(candidate, pixels, width))
                kind = "Table";
            var region = caption is null ? candidate : new SKRectI(
                Math.Min(candidate.Left, caption.Bounds.Left), Math.Min(candidate.Top, caption.Bounds.Top),
                Math.Max(candidate.Right, caption.Bounds.Right), Math.Max(candidate.Bottom, caption.Bounds.Bottom));
            var padding = Math.Max(2, (int)Math.Ceiling(SafePaddingPoints / pointsPerPixel));
            region = new SKRectI(Math.Max(0, region.Left - padding), Math.Max(0, region.Top - padding),
                Math.Min(width, region.Right + padding), Math.Min(height, region.Bottom + padding));
            var sourceScale = Math.Min(sourceCx / (double)width, sourceCy / (double)height);
            var (scale, landscape) = BestFit(region);
            if (sourceScale <= 0) throw new InvalidDataException("Invalid source-page display scale.");
            var rejection = kind is null ? "Insufficient detection confidence" :
                scale / sourceScale < MinimumMagnification ? "Insufficient enlargement gain" :
                !SafeBoundary(region, pixels, width, height) ? "Unsafe crop boundary" :
                results.Any(r => r.Selected && r.Bounds.IntersectsWith(region)) ? "Overlapping selected region" : null;
            var method = caption is not null ? "caption-and-isolated-raster" : kind is not null ? "ruled-grid" : "isolated-raster";
            if (rejection is not null)
            {
                results.Add(new(kind ?? "Unknown", method, ReadOnlyMemory<byte>.Empty, region,
                    checked((long)Math.Round(region.Width * scale)), checked((long)Math.Round(region.Height * scale)),
                    scale / sourceScale, kind is null ? "Low" : "High", rejection) { Landscape = landscape });
                continue;
            }
            using var subset = new SKBitmap();
            if (!bitmap.ExtractSubset(subset, region))
                throw new InvalidDataException("Could not extract the source enlargement region.");
            using var image = SKImage.FromBitmap(subset);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            EmfPerformanceTiming.Count(EmfPerformanceCounter.SupplementsSelected);
            results.Add(new(kind!, method, encoded.ToArray(), region,
                checked((long)Math.Round(region.Width * scale)), checked((long)Math.Round(region.Height * scale)),
                scale / sourceScale, "High", null) { Landscape = landscape });
        }
        return results;
    }

    private sealed record Caption(string Kind, SKRectI Bounds);

    private static List<Caption> Captions(PrintableArtifactTextGeometry geometry, int width, int height)
    {
        var rows = new List<List<PrintableArtifactGlyph>>();
        foreach (var glyph in geometry.Glyphs.Where(g => !string.IsNullOrWhiteSpace(g.Text))
                     .OrderBy(g => g.Baseline).ThenBy(g => g.Left))
        {
            if (rows.Count == 0 || Math.Abs(rows[^1][0].Baseline - glyph.Baseline) > 2)
                rows.Add([]);
            rows[^1].Add(glyph);
        }
        var captions = new List<Caption>();
        foreach (var row in rows)
        {
            // Split simultaneous captions in two columns before parsing text.
            var chunks = new List<List<PrintableArtifactGlyph>> { new() };
            foreach (var glyph in row.OrderBy(g => g.Left))
            {
                if (chunks[^1].Count > 0 && glyph.Left - chunks[^1][^1].Right > Math.Max(18, glyph.FontSize * 2))
                    chunks.Add([]);
                chunks[^1].Add(glyph);
            }
            foreach (var chunk in chunks)
            {
                var text = new StringBuilder();
                PrintableArtifactGlyph? previous = null;
                foreach (var glyph in chunk)
                {
                    if (previous is not null && glyph.Left - previous.Right > Math.Max(1, glyph.FontSize * .18))
                        text.Append(' ');
                    text.Append(glyph.Text);
                    previous = glyph;
                }
                var match = Regex.Match(text.ToString(), @"^(?<kind>Fig(?:ure)?\.?|Table)\s+\d+[A-Za-z]?\s*[.:)]",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!match.Success) continue;
                var left = (int)Math.Floor(chunk.Min(g => g.Left) * width / geometry.Width);
                var top = (int)Math.Floor(chunk.Min(g => g.Top) * height / geometry.Height);
                var right = (int)Math.Ceiling(chunk.Max(g => g.Right) * width / geometry.Width);
                var bottom = (int)Math.Ceiling(chunk.Max(g => g.Bottom) * height / geometry.Height);
                if (left < 0 || top < 0 || right > width || bottom > height || left >= right || top >= bottom) continue;
                captions.Add(new(match.Groups["kind"].Value.StartsWith("Table", StringComparison.OrdinalIgnoreCase)
                    ? "Table" : "Figure", new(left, top, right, bottom)));
            }
        }
        return captions;
    }

    private static bool Near(SKRectI region, SKRectI caption, double pointsPerPixel)
    {
        var overlap = Math.Min(region.Right, caption.Right) - Math.Max(region.Left, caption.Left);
        var distance = Math.Max(0, Math.Max(region.Top - caption.Bottom, caption.Top - region.Bottom));
        return overlap > Math.Min(region.Width, caption.Width) * MinimumCaptionOverlap &&
            distance * pointsPerPixel <= CaptionProximityPoints;
    }

    private static bool HasNonTextGraphics(SKRectI bounds, PrintableArtifactTextGeometry geometry,
        SKColor[] pixels, int width)
    {
        if (!geometry.ContainsGraphics) return false;
        var mask = new bool[checked(bounds.Width * bounds.Height)];
        var height = pixels.Length / width;
        foreach (var glyph in geometry.Glyphs)
        {
            var left = Math.Max(bounds.Left, (int)Math.Floor(glyph.Left * width / geometry.Width) - 2);
            var right = Math.Min(bounds.Right, (int)Math.Ceiling(glyph.Right * width / geometry.Width) + 2);
            var top = Math.Max(bounds.Top, (int)Math.Floor(glyph.Top * height / geometry.Height) - 2);
            var bottom = Math.Min(bounds.Bottom, (int)Math.Ceiling(glyph.Bottom * height / geometry.Height) + 2);
            for (var y = top; y < bottom; y++)
            for (var x = left; x < right; x++) mask[(y - bounds.Top) * bounds.Width + x - bounds.Left] = true;
        }
        var ink = 0;
        var nonText = 0;
        for (var y = bounds.Top; y < bounds.Bottom; y++)
        for (var x = bounds.Left; x < bounds.Right; x++)
        {
            if (!IsInk(pixels[y * width + x])) continue;
            ink++;
            if (!mask[(y - bounds.Top) * bounds.Width + x - bounds.Left]) nonText++;
        }
        return nonText >= MinimumNonTextInkPixels && nonText >= ink * MinimumGraphicInkFraction;
    }

    private static bool IsRuledTable(SKRectI bounds, SKColor[] pixels, int width)
    {
        var horizontal = new bool[bounds.Height];
        var vertical = new bool[bounds.Width];
        for (var y = bounds.Top; y < bounds.Bottom; y++)
        {
            var count = 0;
            for (var x = bounds.Left; x < bounds.Right; x++) if (IsInk(pixels[y * width + x])) count++;
            horizontal[y - bounds.Top] = count >= bounds.Width * RuleCoverage;
        }
        for (var x = bounds.Left; x < bounds.Right; x++)
        {
            var count = 0;
            for (var y = bounds.Top; y < bounds.Bottom; y++) if (IsInk(pixels[y * width + x])) count++;
            vertical[x - bounds.Left] = count >= bounds.Height * RuleCoverage;
        }
        var horizontalLines = Bands(horizontal, 2).ToArray();
        var verticalLines = Bands(vertical, 2).ToArray();
        // Thick filled graphics are not table rules. Require intersecting thin
        // rules across both dimensions, rather than a paragraph's baselines.
        var maxThickness = Math.Max(3, Math.Min(bounds.Width, bounds.Height) / 25);
        if (horizontalLines.Length < MinimumHorizontalRules || verticalLines.Length < MinimumVerticalRules ||
            horizontalLines.Any(b => b.End - b.Start > maxThickness) ||
            verticalLines.Any(b => b.End - b.Start > maxThickness)) return false;
        var populated = 0;
        var populatedRows = new HashSet<int>();
        var populatedColumns = new HashSet<int>();
        for (var row = 0; row < horizontalLines.Length - 1; row++)
        for (var column = 0; column < verticalLines.Length - 1; column++)
        {
            var inkColumns = new bool[verticalLines[column + 1].Start - verticalLines[column].End];
            var inkRows = new bool[horizontalLines[row + 1].Start - horizontalLines[row].End];
            for (var y = horizontalLines[row].End + 2; y < horizontalLines[row + 1].Start - 2; y++)
            for (var x = verticalLines[column].End + 2; x < verticalLines[column + 1].Start - 2; x++)
            {
                var ink = IsInk(pixels[(bounds.Top + y) * width + bounds.Left + x]);
                inkColumns[x - verticalLines[column].End] |= ink;
                inkRows[y - horizontalLines[row].End] |= ink;
            }
            // At least two separated interior marks per cell corroborate values;
            // an empty plot grid or filled rectangle is not a ruled table.
            if (Bands(inkColumns, 2).Count() < 2 && Bands(inkRows, 2).Count() < 2) continue;
            populated++;
            populatedRows.Add(row);
            populatedColumns.Add(column);
        }
        return populated >= MinimumPopulatedCells && populatedRows.Count >= 2 && populatedColumns.Count >= 2;
    }

    private static bool SafeBoundary(SKRectI bounds, SKColor[] pixels, int width, int height)
    {
        // Interior crop edges must cross exact opaque white, including faint
        // marks. A page edge is already the original source boundary.
        for (var x = bounds.Left; x < bounds.Right; x++)
        {
            if (bounds.Top > 0 && pixels[bounds.Top * width + x] != SKColors.White) return false;
            if (bounds.Bottom < height && pixels[(bounds.Bottom - 1) * width + x] != SKColors.White) return false;
        }
        for (var y = bounds.Top; y < bounds.Bottom; y++)
        {
            if (bounds.Left > 0 && pixels[y * width + bounds.Left] != SKColors.White) return false;
            if (bounds.Right < width && pixels[y * width + bounds.Right - 1] != SKColors.White) return false;
        }
        return true;
    }

    private static bool IsInk(SKColor pixel) => pixel.Alpha > 0 &&
        (pixel.Red < 245 || pixel.Green < 245 || pixel.Blue < 245);

    private static IEnumerable<(int Start, int End)> Bands(bool[] occupied, int gap)
    {
        var start = -1;
        var last = -1;
        for (var i = 0; i < occupied.Length; i++)
        {
            if (!occupied[i]) continue;
            if (start < 0) start = i;
            else if (i - last > gap)
            {
                yield return (start, last + 1);
                start = i;
            }
            last = i;
        }
        if (start >= 0) yield return (start, last + 1);
    }
}
