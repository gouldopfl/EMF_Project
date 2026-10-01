using System.Text.RegularExpressions;
using EMF.Core.Models;
using SkiaSharp;

namespace EMF.Extensions.VeteransClaims.Orchestration;

internal sealed record VeteransReviewerNativeRegionPlacement(
    string Kind, int SourceTop, int SourceBottom, int TargetTop, int TargetBottom,
    int HorizontalOffset, IReadOnlyList<string> SourceLines, IReadOnlyList<string> RenderedLines);

// Classify whole regions before changing pixels. Fixed-layout strips are copied
// rigidly; only proven prose receives native word-tile wrapping. Whitespace may
// be compacted between regions, never between rows inside a structured region.
internal sealed record VeteransReviewerNativeEvidencePage(
    ReadOnlyMemory<byte> Content, IReadOnlyList<string> Changes)
{
    public IReadOnlyList<VeteransReviewerNativeRegionPlacement> Regions { get; init; } = [];

    // Plan exclusions over the complete selected note before modifying any
    // reviewer pixels. A section can span pages; an unbounded or interrupted
    // section is left intact. Persisted pages and source artifacts are immutable.
    public static IReadOnlyList<PrintableArtifactPage> SuppressHistoricalMedications(
        IReadOnlyList<PrintableArtifactPage> pages, out int omittedRows)
    {
        omittedRows = 0;
        var rows = new List<(int Page, Row Row)>();
        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            if (page.ContentType != "image/png" || page.SuggestedClockwiseRotation != 0 ||
                page.TextGeometry is not { ContainsGraphics: false, Width: > 0, Height: > 0 } geometry)
                continue;
            rows.AddRange(geometry.Glyphs.Where(g => !string.IsNullOrWhiteSpace(g.Text))
                .GroupBy(g => Math.Round(g.Baseline, 1)).OrderBy(g => g.Key)
                .Select(g => new Row(g.OrderBy(x => x.X).ToArray()))
                .Where(r => !PageFurniture(r, geometry.Height)).Select(r => (i, r)));
        }
        var excluded = new HashSet<Row>();
        for (var i = 0; i < rows.Count; i++)
        {
            if (!Regex.IsMatch(rows[i].Row.Text,
                    @"^(?:MEDICATIONS:|(?:MEDS:\s*)?Active Outpatient Medications.*)$", RegexOptions.IgnoreCase)) continue;
            var end = i + 1;
            while (end < rows.Count && !MedicationEnd(rows[end].Row.Text)) end++;
            if (end == rows.Count) continue;
            var span = rows.Skip(i).Take(end - i).ToArray();
            if (!span.Any(r => Regex.IsMatch(r.Row.Text, @"^(?:MEDS:\s*)?Active Outpatient Medications\b", RegexOptions.IgnoreCase)) ||
                !span.Any(r => Regex.IsMatch(r.Row.Text, @"^\d+\)\s"))) continue;
            var firstPage = rows[i].Page;
            var lastPage = rows[end].Page;
            if (Enumerable.Range(firstPage, lastPage - firstPage + 1).Any(p =>
                    pages[p].TextGeometry is not { ContainsGraphics: false, Width: > 0, Height: > 0 } ||
                    pages[p].ContentType != "image/png" || pages[p].SuggestedClockwiseRotation != 0 ||
                    (p > firstPage && pages[p].PageNumber != pages[p - 1].PageNumber + 1))) continue;
            foreach (var item in span) excluded.Add(item.Row);
            i = end - 1;
        }
        if (excluded.Count == 0) return pages;

        var output = new List<PrintableArtifactPage>();
        for (var i = 0; i < pages.Count; i++)
        {
            var removed = rows.Where(r => r.Page == i && excluded.Contains(r.Row)).Select(r => r.Row).ToArray();
            if (removed.Length == 0) { output.Add(pages[i]); continue; }
            var page = pages[i];
            var geometry = page.TextGeometry!;
            using var bitmap = SKBitmap.Decode(page.Content.Span)
                ?? throw new InvalidDataException("Native evidence image could not be decoded.");
            var pixels = bitmap.Pixels;
            var removedGlyphs = removed.SelectMany(r => r.Glyphs).ToHashSet();
            foreach (var row in removed)
            {
                var box = Bounds(row.Glyphs, bitmap.Width / geometry.Width, bitmap.Height / geometry.Height,
                    bitmap.Width, bitmap.Height);
                // Only the excluded glyph band is blanked; adjacent note rows,
                // repeated patient headers and other source marks are retained.
                for (var y = box.Top; y < box.Bottom; y++)
                    Array.Fill(pixels, SKColors.White, y * bitmap.Width + box.Left, box.Width);
            }
            omittedRows += removed.Length;
            var retainedGlyphs = geometry.Glyphs.Where(g => !removedGlyphs.Contains(g)).ToArray();
            if (rows.Where(r => r.Page == i).All(r => excluded.Contains(r.Row)))
            {
                // Drop a medication-only page only if no unexplained source ink
                // remains after accounting for its repeating header and footer.
                var check = (SKColor[])pixels.Clone();
                var furniture = retainedGlyphs.Where(g => !string.IsNullOrWhiteSpace(g.Text))
                    .GroupBy(g => Math.Round(g.Baseline, 1));
                foreach (var group in furniture)
                {
                    var box = Bounds(group, bitmap.Width / geometry.Width, bitmap.Height / geometry.Height,
                        bitmap.Width, bitmap.Height);
                    for (var y = box.Top; y < box.Bottom; y++)
                        Array.Fill(check, SKColors.White, y * bitmap.Width + box.Left, box.Width);
                }
                if (!HasInk(check, bitmap.Width, 0, bitmap.Height)) continue;
            }
            bitmap.Pixels = pixels;
            using var image = SKImage.FromBitmap(bitmap);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            output.Add(new() { PageNumber = page.PageNumber, ContentType = page.ContentType,
                Content = png.ToArray(), TextGeometry = geometry with { Glyphs = retainedGlyphs } });
        }
        return output;
    }

    private static bool PageFurniture(Row row, double height) =>
        (row.Baseline < height * .05 && row.Text.Contains("Date of birth:", StringComparison.Ordinal)) ||
        (row.Baseline > height * .95 && Regex.IsMatch(row.Text,
            @"^Report generated by My HealtheVet on VA\.gov on .+\s?Page \d+ of \d+$"));

    private static bool MedicationEnd(string text) => Regex.IsMatch(text,
        @"^(?:Columbia Suicide Severity Rating Scale|C-SSRS|PHYSICAL EXAM:|ASSESSMENT(?:/PLAN)?:|REVIEW OF SYSTEMS|ROS:|OBJECTIVE:|EXAM:|PLAN:|VITAL SIGNS:|ALLERGIES:|/es/|Signed:|\d{2}/\d{2}/\d{4} ADDENDUM)",
        RegexOptions.IgnoreCase) ||
        (Regex.IsMatch(text, @"^[A-Z][A-Z /()-]{2,54}:$") &&
         !text.Contains("MEDICATION", StringComparison.Ordinal));

    public static VeteransReviewerNativeEvidencePage Prepare(PrintableArtifactPage page, bool isBlueButton, string? artifactTitle = null)
    {
        if (!isBlueButton || page.TextGeometry is not { } geometry || geometry.ContainsGraphics ||
            page.SuggestedClockwiseRotation != 0 || geometry.Width <= 0 || geometry.Height <= 0)
            return new(page.Content, []);

        using var bitmap = SKBitmap.Decode(page.Content.Span)
            ?? throw new InvalidDataException("Native evidence image could not be decoded.");
        var width = bitmap.Width;
        var height = bitmap.Height;
        var pixels = bitmap.Pixels;
        var sx = width / geometry.Width;
        var sy = height / geometry.Height;
        var changes = new List<string>();
        var rows = geometry.Glyphs.Where(g => !string.IsNullOrWhiteSpace(g.Text))
            .GroupBy(g => Math.Round(g.Baseline, 1)).OrderBy(g => g.Key)
            .Select(g => new Row(g.OrderBy(l => l.X).ToArray())).ToList();

        foreach (var row in rows.Where(r => r.Baseline > geometry.Height * .95).ToArray())
        {
            if (!Regex.IsMatch(row.Text, @"^Report generated by My HealtheVet on VA\.gov on .+Page \d+ of \d+$")) continue;
            var box = Bounds(row.Glyphs, sx, sy, width, height);
            for (var y = box.Top; y < box.Bottom; y++)
                Array.Fill(pixels, SKColors.White, y * width + box.Left, box.Width);
            rows.Remove(row);
            changes.Add($"Removed native Blue Button footer at y={row.Baseline:F1}pt.");
        }

        MarkProseContinuations(rows);
        var regions = DetectRegions(rows, sx, sy, width, height);
        // Any ink not explained by native glyph bounds is a protected region.
        // This also retains marks on pages with incomplete glyph information.
        var complete = new List<Region>();
        var cursor = 0;
        foreach (var region in regions)
        {
            if (HasInk(pixels, width, cursor, region.Top))
                complete.Add(new([], false, cursor, region.Top));
            complete.Add(region);
            cursor = region.Bottom;
        }
        if (HasInk(pixels, width, cursor, height)) complete.Add(new([], false, cursor, height));

        var output = new SKColor[pixels.Length];
        Array.Fill(output, SKColors.White);
        var placements = new List<VeteransReviewerNativeRegionPlacement>();
        var targetY = 0;
        cursor = 0;
        var maxGap = (int)Math.Ceiling(24 * sy);
        var removedWhiteRows = 0;
        foreach (var region in complete)
        {
            var gap = region.Top - cursor;
            // Every gap here was checked to contain no ink. No scan or deletion
            // of white bands takes place inside a classified region.
            var retainedGap = Math.Min(gap, maxGap);
            targetY += retainedGap;
            removedWhiteRows += gap - retainedGap;
            var rendered = pixels[(region.Top * width)..(region.Bottom * width)];
            var renderedLines = region.Rows.Select(r => r.Text).ToArray();
            if (region.Narrative)
            {
                var replacements = new List<Replacement>();
                var sentences = ProseRuns(region.Rows);
                var displayLines = new List<string>();
                foreach (var run in sentences)
                {
                    var replacement = run.Length > 1
                        ? Repack(run, geometry.Width - 20, pixels, width, height, sx, sy) : null;
                    if (replacement is null) displayLines.AddRange(run.Select(r => r.Text));
                    else
                    {
                        replacements.Add(replacement);
                        displayLines.AddRange(replacement.Lines);
                        changes.Add($"Joined native prose at y={run[0].Baseline:F1}pt: {run.Length} source rows, {replacement.Lines.Length} reviewer rows; {run.Sum(r => r.Words.Count)} original word tiles.");
                    }
                }
                foreach (var replacement in replacements.OrderByDescending(r => r.Top))
                {
                    var localTop = replacement.Top - region.Top;
                    var localBottom = replacement.Bottom - region.Top;
                    var shortened = new SKColor[rendered.Length - (localBottom - localTop - replacement.Height) * width];
                    Array.Copy(rendered, 0, shortened, 0, localTop * width);
                    Array.Copy(replacement.Pixels, 0, shortened, localTop * width, replacement.Pixels.Length);
                    Array.Copy(rendered, localBottom * width, shortened, (localTop + replacement.Height) * width,
                        rendered.Length - localBottom * width);
                    rendered = shortened;
                }
                renderedLines = displayLines.ToArray();
            }
            Array.Copy(rendered, 0, output, targetY * width, rendered.Length);
            var targetBottom = targetY + rendered.Length / width;
            placements.Add(new(region.Narrative ? "Narrative" : "Structured", region.Top, region.Bottom,
                targetY, targetBottom, 0, region.Rows.Select(r => r.Text).ToArray(), renderedLines));
            targetY = targetBottom;
            cursor = region.Bottom;
        }
        if (targetY == 0) return new(VeteransReviewerSourcePageCrop.Crop(page.Content).Content, changes);
        if (removedWhiteRows > 0)
            changes.Add($"Removed {removedWhiteRows} all-white raster rows between regions; structured X/Y geometry preserved.");
        // Keep the outer-crop safety margin below the final protected region.
        targetY += Math.Min(height - cursor, Math.Max(2, (int)Math.Ceiling(Math.Min(width, height) * .01)));
        using var result = new SKBitmap(width, targetY);
        result.Pixels = output[..(targetY * width)];
        using var image = SKImage.FromBitmap(result);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        var crop = VeteransReviewerSourcePageCrop.Crop(encoded.ToArray());
        var presented = PresentSourceHeadings(crop, rows, placements, sx, sy, width, height,
            geometry.Height, artifactTitle, changes);
        return new(presented, changes)
        {
            Regions = placements.Select(r => r with { TargetTop = r.TargetTop - crop.Top,
                TargetBottom = r.TargetBottom - crop.Top, HorizontalOffset = -crop.Left }).ToArray()
        };
    }

    // Apply presentation changes after reconstruction and its crop have been
    // fixed. Keeping those dimensions prevents title removal from magnifying
    // the remaining clinical text in Word's fit-to-page layout.
    private static ReadOnlyMemory<byte> PresentSourceHeadings(
        VeteransReviewerSourcePageCrop crop, IReadOnlyList<Row> rows,
        List<VeteransReviewerNativeRegionPlacement> placements,
        double sx, double sy, int width, int height, double sourceHeight,
        string? artifactTitle, List<string> changes)
    {
        var bodySizes = rows.Where(r => r.IsMono && r.Text.Any(char.IsLower))
            .SelectMany(r => r.Glyphs).Select(g => g.FontSize).Order().ToArray();
        if (bodySizes.Length == 0)
            bodySizes = rows.Where(r => r.Text.Any(char.IsLower)).SelectMany(r => r.Glyphs)
                .Where(g => !g.Font.Contains("Bold", StringComparison.OrdinalIgnoreCase))
                .Select(g => g.FontSize).Order().ToArray();
        if (bodySizes.Length == 0) return crop.Content;
        var bodySize = bodySizes[bodySizes.Length / 2];
        var opening = rows.Where(r => !PageFurniture(r, sourceHeight)).FirstOrDefault();
        var duplicate = new HashSet<Row>();
        if (!string.IsNullOrWhiteSpace(artifactTitle) && opening is not null &&
            opening.Size > bodySize * 1.1 && opening.Font.Contains("Bold", StringComparison.OrdinalIgnoreCase))
        {
            // A wrapped opening title is one title. Never search further down
            // the note or consume metadata / a clinical section to get a match.
            var candidates = rows.SkipWhile(r => r != opening).TakeWhile((r, index) =>
                r.Font == opening.Font && Math.Abs(r.Size - opening.Size) < .1 &&
                r.Baseline - opening.Baseline <= index * opening.Size * 1.75 &&
                (!r.Text.Contains(':') ||
                 (index == 0 && Regex.IsMatch(r.Text, @"^[A-Z]{2,5}\s*:\s*[A-Z]")))).Take(3).ToArray();
            for (var count = 1; count <= candidates.Length; count++)
                if (VeteransReviewerNativeProse.EquivalentOpeningTitle(
                        string.Join(" ", candidates.Take(count).Select(r => r.Text)), artifactTitle))
                {
                    duplicate.UnionWith(candidates.Take(count));
                    break;
                }
        }
        using var bitmap = SKBitmap.Decode(crop.Content.Span)!;
        using var canvas = new SKCanvas(bitmap);
        foreach (var row in rows)
        {
            var remove = duplicate.Contains(row);
            var section = MajorHeading(row) || row.Text is "Details" or "Note";
            if (!remove && (!section || row.Size <= bodySize * 1.1)) continue;
            // Structured strips preserve each row's original local coordinates.
            // Ambiguous or reflowed rows are deliberately left untouched.
            var box = Bounds(row.Glyphs, sx, sy, width, height);
            var placement = placements.FirstOrDefault(p => p.Kind == "Structured" &&
                box.Top >= p.SourceTop && box.Bottom <= p.SourceBottom);
            if (placement is null) continue;
            var target = SKRectI.Create(box.Left - crop.Left,
                box.Top + placement.TargetTop - placement.SourceTop - crop.Top, box.Width, box.Height);
            using var tile = new SKBitmap();
            if (!bitmap.ExtractSubset(tile, target)) continue;
            using var preserved = tile.Copy();
            using var white = new SKPaint { Color = SKColors.White };
            canvas.DrawRect(target, white);
            if (!remove)
            {
                var scale = bodySize / row.Size;
                using var image = SKImage.FromBitmap(preserved);
                canvas.DrawImage(image, new SKRect(target.Left, target.Top,
                    target.Left + (float)(target.Width * scale), target.Top + (float)(target.Height * scale)),
                    new SKSamplingOptions(SKFilterMode.Linear));
                changes.Add($"Restrained native section heading '{row.Text}' from {row.Size:F1}pt to {bodySize:F1}pt; source words retained.");
            }
            else
            {
                changes.Add($"Suppressed duplicate opening source title '{row.Text}'; equivalent to reviewer artifact title.");
                var index = placements.IndexOf(placement);
                placements[index] = placement with { RenderedLines = placement.RenderedLines.Where(l => l != row.Text).ToArray() };
            }
        }
        canvas.Flush();
        using var finalImage = SKImage.FromBitmap(bitmap);
        using var encoded = finalImage.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private static bool HasInk(SKColor[] pixels, int width, int top, int bottom)
    {
        for (var i = top * width; i < bottom * width; i++)
            if (pixels[i] != SKColors.White) return true;
        return false;
    }

    // A local prose label is not a fixed-layout section. Its original pixels
    // stay with the narrative. No particular clinician or source page is special.
    private static bool NarrativeLead(Row row)
    {
        if (row.ProseContinuation || !Regex.IsMatch(row.Text, @"^[A-Za-z][A-Za-z /()-]{0,39}:\s")) return false;
        var value = row.Text[(row.Text.IndexOf(':') + 1)..].Trim();
        // Sentence-like values may flow with prose. A lengthy label cannot
        // turn a numeric result or short code value into a narrative lead.
        return value.Length > 0 && char.IsLetter(value[0]) &&
            value.Count(char.IsLower) > 15 &&
            value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 5;
    }
    private static bool ListOrRule(Row row) => Regex.IsMatch(row.Text, @"^(?:[-*•]|\d+[.)]\s|[=_]{3,}|/es/)");
    private static bool Field(Row row) => !row.ProseContinuation && Regex.IsMatch(row.Text, @"^[A-Za-z][A-Za-z /()-]{0,39}:") &&
        !NarrativeLead(row) && !NarrativeSection(row);
    private static bool MajorHeading(Row row) => !row.ProseContinuation && Regex.IsMatch(row.Text, @"^[A-Za-z][A-Za-z0-9 ()/–-]{0,54}:$");
    private static bool NarrativeSection(Row row) => Regex.IsMatch(row.Text,
        @"^(?:[SAP]|HPI|Attending|History|Assessment|Plan|Mental Status(?: Examination)?):\s",
        RegexOptions.IgnoreCase);
    private static bool FixedSectionHeading(Row row) => MajorHeading(row) && Regex.IsMatch(row.Text,
        @"^(?:PHYSICAL EXAM|MENTAL STATUS.*|OBJECTIVE|O|VITAL.*|LAB.*|RESULTS.*|.*MEDICATION.*|.*ALLERG.*|RX TODAY|GOALS.*|SOCIAL|.*SIGNATURE.*):$",
        RegexOptions.IgnoreCase);
    private static bool TableHeader(Row row) => Regex.Matches(row.Text,
        @"\b(?:Result|Units?|Reference|Range|Specimen|Collection|Ref)\b", RegexOptions.IgnoreCase).Count >= 3;
    private static bool AddendumHeader(Row row) =>
        Regex.IsMatch(row.Text, @"^\d{2}/\d{2}/\d{4} ADDENDUM\b");
    private static bool ProseStart(Row row) => row.IsMono && !ListOrRule(row) && !Field(row) &&
        !TableHeader(row) && row.Words.Count >= 5 && row.Text.Count(char.IsLower) > 15;
    private static bool NarrativeDashListStart(Row row) => row.IsMono &&
        Regex.IsMatch(row.Text, @"^-\s?[A-Za-z]") &&
        row.Words.Count >= 5 && row.Text.Count(char.IsLower) > 15 &&
        !Field(row) && !TableHeader(row);

    // A sentence-like field label does not make its stacked code values prose.
    // Keep their explicit rows intact; mixed-case narrative remains eligible.
    private static bool StackedFieldValues(IReadOnlyList<Row> rows) => rows.Count > 1 &&
        NarrativeLead(rows[0]) && UppercaseValue(rows[0].Text[(rows[0].Text.IndexOf(':') + 1)..]) &&
        rows.Skip(1).All(r => UppercaseValue(r.Text));

    private static bool UppercaseValue(string text) => text.Any(char.IsLetter) && !text.Any(char.IsLower);

    private static void MarkProseContinuations(IReadOnlyList<Row> rows)
    {
        for (var i = 1; i + 1 < rows.Count; i++)
        {
            var row = rows[i];
            var previous = rows[i - 1];
            var next = rows[i + 1];
            if (!ProseStart(previous) || !row.IsMono || row.Font != previous.Font ||
                Math.Abs(row.Size - previous.Size) > .1 || Math.Abs(row.X - previous.X) > 1 ||
                row.Baseline - previous.Baseline <= row.Size ||
                row.Baseline - previous.Baseline >= row.Size * 1.75) continue;

            // A short metric label after a comma in running prose, followed by
            // its numeric value, is a line wrap, not a new clinical section.
            var inlineMetric = previous.Text.EndsWith(',') &&
                Regex.IsMatch(row.Text, @"^[A-Z]{2,5}:$") &&
                Regex.IsMatch(next.Text, @"^\d+(?:\.\d+)?[,;] ") &&
                next.Font == row.Font && Math.Abs(next.Size - row.Size) < .1 &&
                Math.Abs(next.X - row.X) < 1 &&
                next.Baseline - row.Baseline > row.Size &&
                next.Baseline - row.Baseline < row.Size * 1.75;
            var inlineLowercaseLabel = char.IsLower(row.Text[0]) &&
                Regex.IsMatch(row.Text, @"^[a-z]+:\S") &&
                row.Words.Count >= 5 && row.Text.Count(char.IsLower) > 15 &&
                Regex.IsMatch(previous.Text, @"\b(?:the|as|is|was|were|are|with|and|of)$");
            row.ProseContinuation = inlineMetric || inlineLowercaseLabel;
        }
    }

    private static bool AlignedColumns(IReadOnlyList<Row> rows)
    {
        // Spaces are only candidate gaps. A column additionally requires the
        // same native X anchor on different baselines with different left cells.
        // A single row with repeated spaces never establishes a column.
        var anchors = rows.SelectMany((row, index) => row.ColumnAnchors.Select(x =>
            (Row: index, X: x, Prefix: string.Concat(row.Glyphs.Where(g => g.X < x).Select(g => g.Text))))).ToArray();
        return anchors.Any(a => anchors.Any(b => b.Row != a.Row && Math.Abs(a.X - b.X) < 1 && a.Prefix != b.Prefix));
    }

    private static List<Region> DetectRegions(IReadOnlyList<Row> rows, double sx, double sy, int width, int height)
    {
        var groups = new List<List<Row>>();
        foreach (var row in rows)
        {
            var current = groups.LastOrDefault();
            var split = current is null;
            if (current is not null)
            {
                var previous = current[^1];
                var gap = row.Baseline - previous.Baseline;
                var protectedSection = current.Any(r => FixedSectionHeading(r) || TableHeader(r));
                var protectedFlow = protectedSection || current.Any(r => Field(r) || ListOrRule(r)) || AlignedColumns(current);
                var objectiveSubheading = row.Text == "VITAL SIGNS:" && current.Any(r => r.Text is "OBJECTIVE:" or "O:");
                split = row.Font != previous.Font || Math.Abs(row.Size - previous.Size) > .1 ||
                    ((NarrativeLead(row) || (Field(row) && current.Count == 1 && UppercaseValue(current[0].Text))) &&
                     (!protectedSection || NarrativeSection(row))) ||
                    (previous.Text.EndsWith('?') && ProseStart(row) && row.X > previous.X) ||
                    (NarrativeDashListStart(row) && current.Count > 0) ||
                    (current.Count == 1 && (MajorHeading(current[0]) || AddendumHeader(current[0])) && !protectedSection && ProseStart(row)) ||
                    ((ListOrRule(row) || TableHeader(row)) && (ProseStart(current[0]) || NarrativeDashListStart(current[0]))) ||
                    (MajorHeading(row) && !objectiveSubheading) ||
                    AddendumHeader(row) ||
                    (gap > Math.Max(row.Size, previous.Size) * 1.75 && (!protectedFlow || (ProseStart(row) && !protectedSection)));
            }
            if (split) groups.Add([row]); else current!.Add(row);
        }
        var regions = new List<Region>();
        foreach (var group in groups)
        {
            var first = group[0];
            var narrativeDashList = NarrativeDashListStart(first);
            var continuationIndentTolerance =
                narrativeDashList
                    ? Math.Max(18, first.Size * 2.5)
                    : 1;
            var narrative = (ProseStart(first) || narrativeDashList) &&
                !StackedFieldValues(group) && !AlignedColumns(group) &&
                group.All(r => r.IsMono && r.Font == first.Font &&
                    Math.Abs(r.Size - first.Size) < .1 &&
                    (Math.Abs(r.X - first.X) < continuationIndentTolerance ||
                     // Blue Button sometimes places short wrapped prose fragments
                     // back at the left margin inside otherwise consistently indented
                     // narrative, including ProVation-style dash-led procedure text.
                     // Accept only tightly constrained short word fragments; fields,
                     // tables and lists remain protected.
                     NarrativeMarginFragment(r, first)) &&
                    !Field(r) && !TableHeader(r)) &&
                (narrativeDashList
                    ? group.Skip(1).All(r => !ListOrRule(r))
                    : group.All(r => !ListOrRule(r))) &&
                group.Skip(1).Select((r, i) => r.Baseline - group[i].Baseline).All(g => g > first.Size && g < first.Size * 1.75);
            var box = Bounds(group.SelectMany(r => r.Glyphs), sx, sy, width, height);
            var region = new Region(group.ToArray(), narrative, box.Top, box.Bottom);
            if (regions.Count > 0 && region.Top < regions[^1].Bottom)
            {
                var previous = regions[^1];
                regions[^1] = new(previous.Rows.Concat(region.Rows).ToArray(), false, previous.Top,
                    Math.Max(previous.Bottom, region.Bottom));
            }
            else regions.Add(region);
        }
        return regions;
    }

    private static bool NarrativeMarginFragment(Row row, Row first) =>
        row.Words.Count <= 2 &&
        row.X < first.X &&
        first.X - row.X <= first.Size * 12 &&
        row.Text.Any(char.IsLower) &&
        !row.Text.EndsWith(':') &&
        !Regex.IsMatch(row.Text, @"^\d");

    private static IReadOnlyList<Row[]> ProseRuns(Row[] rows)
    {
        var result = new List<Row[]>();
        var start = 0;
        var dashLedNarrative = rows.Length > 0 && NarrativeDashListStart(rows[0]);
        for (var i = 0; i < rows.Length; i++)
        {
            // Keep explicit complete-sentence source lines as boundaries. An
            // honorific fragment such as "Dr." is not a paragraph terminator.
            var text = rows[i].Text;
            var complete = Regex.IsMatch(text, "[.!?][\\\"')]*$") &&
                !Regex.IsMatch(text, @"^(?:Dr|Mr|Mrs|Ms|Prof|vs|etc)\.$", RegexOptions.IgnoreCase);
            var trailingMarginContinuation = complete && dashLedNarrative &&
                i + 1 < rows.Length && NarrativeMarginFragment(rows[i + 1], rows[start]);
            if (i == rows.Length - 1 || (complete && !trailingMarginContinuation))
            { result.Add(rows[start..(i + 1)]); start = i + 1; }
        }
        return result;
    }

    private static Replacement? Repack(IReadOnlyList<Row> rows, double right,
        SKColor[] source, int width, int height, double sx, double sy)
    {
        var words = rows.SelectMany(r => r.Words).ToArray();
        var advances = words.Select(w => w.Max(g => g.EndX) - w.Min(g => g.X)).ToArray();
        var space = rows[0].Glyphs.Where(g => g.Text.Length == 1 && g.EndX > g.X)
            .Select(g => g.EndX - g.X).Order().DefaultIfEmpty(rows[0].Size * .6).ToArray();
        var wordSpace = space[space.Length / 2];
        var pitchValues = rows.Skip(1).Select((r, i) => r.Baseline - rows[i].Baseline).Order().ToArray();
        var pitch = pitchValues[pitchValues.Length / 2];
        List<List<int>> linePlan = [[]];
        var available = right - rows[0].X;
        double used = 0;
        for (var i = 0; i < words.Length; i++)
        {
            var fit = advances[i];
            if (string.Concat(words[i].Select(g => g.Text)).Length == 1 && i + 1 < words.Length)
                fit += wordSpace + advances[i + 1];
            if (linePlan[^1].Count > 0 && used + wordSpace + fit > available)
            { linePlan.Add([]); used = 0; }
            if (linePlan[^1].Count > 0) used += wordSpace;
            linePlan[^1].Add(i); used += advances[i];
        }
        if (linePlan.Count > 1 && linePlan[^1].Count == 1 && linePlan[^2].Count > 1)
        {
            var lastWord = linePlan[^2][^1];
            if (advances[lastWord] + wordSpace + advances[linePlan[^1][0]] <= available)
            { linePlan[^2].RemoveAt(linePlan[^2].Count - 1); linePlan[^1].Insert(0, lastWord); }
        }
        var lines = linePlan.Select(line => string.Join(" ", line.Select(i => string.Concat(words[i].Select(g => g.Text))))).ToArray();
        if (lines.Length > rows.Count || lines.SequenceEqual(rows.Select(r => r.Text))) return null;
        var bounds = Bounds(rows.SelectMany(r => r.Glyphs), sx, sy, width, height);
        var top = bounds.Top;
        var bottom = bounds.Bottom;
        var target = Enumerable.Repeat(SKColors.White, width * (bottom - top)).ToArray();
        var covered = new bool[target.Length];
        var lastInk = 0;
        for (var lineIndex = 0; lineIndex < linePlan.Count; lineIndex++)
        {
            var x = rows[0].X;
            var baseline = rows[0].Baseline + pitch * lineIndex;
            foreach (var wordIndex in linePlan[lineIndex])
            {
                var word = words[wordIndex];
                var box = Bounds(word, sx, sy, width, height);
                var dx = (int)Math.Round(x * sx) - (int)Math.Round(word.Min(g => g.X) * sx);
                var dy = (int)Math.Round(baseline * sy) - (int)Math.Round(word[0].Baseline * sy);
                for (var yy = box.Top; yy < box.Bottom; yy++)
                for (var xx = box.Left; xx < box.Right; xx++)
                {
                    var color = source[yy * width + xx];
                    if (color == SKColors.White) continue;
                    var srcIndex = (yy - top) * width + xx;
                    var tx = xx + dx; var ty = yy + dy - top;
                    if (covered[srcIndex] || tx < 0 || tx >= width || ty < 0 || ty >= bottom - top ||
                        target[ty * width + tx] != SKColors.White) return null;
                    covered[srcIndex] = true;
                    target[ty * width + tx] = color;
                    lastInk = Math.Max(lastInk, ty);
                }
                x += advances[wordIndex] + wordSpace;
            }
        }
        for (var y = top; y < bottom; y++)
        for (var xx = 0; xx < width; xx++)
            if (source[y * width + xx] != SKColors.White && !covered[(y - top) * width + xx]) return null;
        var targetHeight = Math.Min(bottom - top, lastInk + 3);
        return new(top, bottom, targetHeight, target[..(targetHeight * width)], lines);
    }

    private static SKRectI Bounds(IEnumerable<PrintableArtifactGlyph> glyphs,
        double sx, double sy, int width, int height)
    {
        var letters = glyphs.ToArray();
        return new(Math.Max(0, (int)Math.Floor(letters.Min(g => g.Left) * sx) - 2),
            Math.Max(0, (int)Math.Floor(letters.Min(g => g.Top) * sy) - 2),
            Math.Min(width, (int)Math.Ceiling(letters.Max(g => g.Right) * sx) + 2),
            Math.Min(height, (int)Math.Ceiling(letters.Max(g => g.Bottom) * sy) + 2));
    }

    private sealed record Replacement(int Top, int Bottom, int Height, SKColor[] Pixels, string[] Lines);
    private sealed record Region(Row[] Rows, bool Narrative, int Top, int Bottom);

    private sealed class Row
    {
        public PrintableArtifactGlyph[] Glyphs { get; }
        public List<PrintableArtifactGlyph[]> Words { get; } = [];
        public string Text { get; }
        public double Baseline => Glyphs[0].Baseline;
        public double X => Glyphs[0].X;
        public double Size => Glyphs[0].FontSize;
        public string Font => Glyphs[0].Font;
        public bool IsMono => (Font.Contains("Mono", StringComparison.OrdinalIgnoreCase) ||
            Font.Contains("Courier", StringComparison.OrdinalIgnoreCase)) &&
            Glyphs.All(g => g.Font == Font && Math.Abs(g.FontSize - Size) < .1);
        public List<double> ColumnAnchors { get; } = [];
        public bool ProseContinuation { get; set; }
        public Row(PrintableArtifactGlyph[] glyphs)
        {
            Glyphs = glyphs;
            var word = new List<PrintableArtifactGlyph>();
            var text = "";
            PrintableArtifactGlyph? previous = null;
            foreach (var g in glyphs)
            {
                var gap = previous is null ? 0 : g.X - previous.EndX;
                if (gap > Size * .15)
                {
                    Words.Add(word.ToArray()); word.Clear(); text += " ";
                    if (gap > Size * 1.6) ColumnAnchors.Add(g.X);
                }
                word.Add(g); text += g.Text; previous = g;
            }
            if (word.Count > 0) Words.Add(word.ToArray());
            Text = text;
        }
    }
}
