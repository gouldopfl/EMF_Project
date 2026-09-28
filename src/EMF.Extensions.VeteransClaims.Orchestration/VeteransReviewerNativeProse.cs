using System.Text.RegularExpressions;
using EMF.Core.Models;

namespace EMF.Extensions.VeteransClaims.Orchestration;

// A hanging narrative has long lines at a common indent and short overflow
// fragments at the margin on separate baselines. Those fragments are not cells.
// This detector uses geometry and typography, never record titles or page IDs.
internal static class VeteransReviewerNativeProse
{
    internal sealed record Line(string Text, double X, double Baseline, double Size,
        string Font, bool Columns, int Page)
    {
        public string FixedText { get; init; } = Text;
        public bool Mono => Font.Contains("Mono", StringComparison.OrdinalIgnoreCase) ||
            Font.Contains("Courier", StringComparison.OrdinalIgnoreCase);
        public bool Field => Regex.IsMatch(Text, @"^[A-Za-z][A-Za-z /()'-]{0,70}:");
        public bool Fragment => Mono && !Columns && !Field && Text.Any(char.IsLower) &&
            Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 2 &&
            !Regex.IsMatch(Text, @"^(?:\d|[-*/])") && !Text.EndsWith(':');
    }

    internal static IReadOnlyList<Line> Lines(PrintableArtifactPage page)
    {
        var result = new List<Line>();
        foreach (var row in page.TextGeometry!.Glyphs.Where(g => !string.IsNullOrWhiteSpace(g.Text))
                     .GroupBy(g => Math.Round(g.Baseline, 1)).OrderBy(g => g.Key))
        {
            var glyphs = row.OrderBy(g => g.X).ToArray();
            var text = "";
            var fixedText = "";
            var columns = false;
            PrintableArtifactGlyph? previous = null;
            foreach (var glyph in glyphs)
            {
                var gap = previous is null ? 0 : glyph.X - previous.EndX;
                if (gap > glyph.FontSize * .15)
                {
                    text += " ";
                    fixedText += new string(' ', Math.Max(1, (int)Math.Round(gap / (glyph.FontSize * .6))));
                }
                columns |= gap > glyph.FontSize * 1.6;
                text += glyph.Text;
                fixedText += glyph.Text;
                previous = glyph;
            }
            var first = glyphs[0];
            result.Add(new(text, first.X, first.Baseline, first.FontSize, first.Font, columns, page.PageNumber) { FixedText = fixedText });
        }
        return result;
    }

    internal static bool EquivalentOpeningTitle(string source, string artifact)
    {
        static string Normalize(string value, bool sourceTitle)
        {
            value = Regex.Replace(value.Trim(), @"\s*[—–-]\s*Continued\s*$", "", RegexOptions.IgnoreCase);
            if (sourceTitle)
                value = Regex.Replace(value, @"^[A-Z]{2,5}\s*:\s*", "", RegexOptions.CultureInvariant);
            return Regex.Replace(value.ToUpperInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
        }
        var codedSource = Regex.IsMatch(source.Trim(), @"^[A-Z]{2,5}\s*:\s*[A-Z]",
            RegexOptions.CultureInvariant);
        var normalizedSource = Normalize(source, true);
        var normalizedArtifact = Normalize(artifact, false);
        return normalizedSource.Length >= 12 &&
            (normalizedSource == normalizedArtifact ||
             (codedSource && normalizedArtifact.StartsWith(normalizedSource + " ", StringComparison.Ordinal)));
    }

    public static IReadOnlyList<string>? Reconstruct(IReadOnlyList<PrintableArtifactPage> pages)
    {
        if (pages.Count == 0 || pages.Any(p => p.ContentType != "image/png" ||
            p.SuggestedClockwiseRotation != 0 ||
            p.TextGeometry is not { ContainsGraphics: false, Width: > 0, Height: > 0 })) return null;
        var repeatedHeaderKeys = pages.SelectMany(page => Lines(page)
                .Where(row => row.Baseline <= page.TextGeometry!.Height * .05 &&
                    VeteransReviewerPatientHeader.IsIdentity(row.Text))
                .Select(row => (Key: VeteransReviewerPatientHeader.IdentityKey(row.Text), page.PageNumber)))
            .Distinct().GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var patientHeaders = new List<string>();
        var seenPatientHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var narrativeRowsByPage = new List<Line>();
        foreach (var page in pages)
        {
            var pageRows = Lines(page);
            for (var i = 0; i < pageRows.Count; i++)
            {
                var row = pageRows[i];
                // Selected excerpts can start below the original page header.
                // A first encounter with a header on a continuation page is still
                // furniture: retain it as opening source identity, not within the record.
                var isPatientHeader = false;
                if (row.Baseline <= page.TextGeometry!.Height * .05 &&
                    row.Text.Contains("Date of birth:", StringComparison.OrdinalIgnoreCase))
                {
                    var header = row.Text;
                    for (var end = i; end < Math.Min(i + 3, pageRows.Count); end++)
                    {
                        if (end > i)
                        {
                            if (pageRows[end].Baseline - pageRows[end - 1].Baseline >
                                Math.Max(pageRows[end].Size, pageRows[end - 1].Size) * 1.8) break;
                            header += " " + pageRows[end].Text;
                        }
                        if (!VeteransReviewerPatientHeader.IsIdentity(header)) continue;
                        var key = VeteransReviewerPatientHeader.IdentityKey(header);
                        // A partial excerpt may contain only one header. In that
                        // case require a page-leading, differently styled identity
                        // between clinical continuation fragments. Structured blocks
                        // additionally need content at the preceding page's bottom
                        // edge; their fields may have a small hanging indent.
                        var before = narrativeRowsByPage.LastOrDefault();
                        var after = end + 1 < pageRows.Count ? pageRows[end + 1] : null;
                        var interruptsProse = i == 0 && !row.Mono &&
                            before is { Mono: true, Columns: false, Field: false } &&
                            after is { Mono: true, Columns: false, Field: false } &&
                            before.Page + 1 == row.Page && Math.Abs(before.X - after.X) < 1 &&
                            !Regex.IsMatch(before.Text, @"[.!?:;][\""')]*$") &&
                            after.Text.Length > 0 && char.IsLower(after.Text[0]);
                        var interruptsPageEdgeBlock = i == 0 && !row.Mono &&
                            before is { Mono: true } && after is { Mono: true } &&
                            before.Page + 1 == row.Page && before.Font == after.Font &&
                            Math.Abs(before.Size - after.Size) < .1 &&
                            before.X > row.X && after.X > row.X &&
                            Math.Abs(before.X - after.X) <= before.Size * 2 &&
                            pages.Any(previousPage => previousPage.PageNumber == before.Page &&
                                before.Baseline >= previousPage.TextGeometry!.Height * .9);
                        if (!repeatedHeaderKeys.Contains(key) && !seenPatientHeaders.Contains(key) &&
                            !interruptsProse && !interruptsPageEdgeBlock) break;
                        if (seenPatientHeaders.Add(key))
                            patientHeaders.Add(header);
                        i = end;
                        isPatientHeader = true;
                        break;
                    }
                }
                if (isPatientHeader) continue;
                if (!(row.Baseline > page.TextGeometry!.Height * .95 &&
                    Regex.IsMatch(row.Text, @"^Report generated by My HealtheVet on VA\.gov on .+Page \d+ of \d+$")))
                    narrativeRowsByPage.Add(row);
            }
        }
        var rows = narrativeRowsByPage.ToArray();
        var candidates = new List<double>();
        for (var i = 1; i + 1 < rows.Length; i++)
        {
            var row = rows[i]; var before = rows[i - 1]; var after = rows[i + 1];
            if (row.Fragment && before.Mono && after.Mono && !before.Columns && !after.Columns &&
                Math.Abs(before.X - after.X) < 1 && before.X - row.X > row.Size * 3 &&
                before.Font == row.Font && after.Font == row.Font &&
                Math.Abs(before.Size - row.Size) < .1 && Math.Abs(after.Size - row.Size) < .1 &&
                before.Text.Any(char.IsLower) && after.Text.Any(char.IsLower)) candidates.Add(before.X);
        }
        var anchor = candidates.GroupBy(x => Math.Round(x)).OrderByDescending(g => g.Count())
            .FirstOrDefault()?.ToArray();
        if (anchor is null || anchor.Length < 2)
        {
            if (pages.Count < 2) return null;
            // Ordinary multi-page text-only clinical notes do not always contain the
            // outdented fragments that identify ProVation-style prose. Use a
            // conservative aligned-narrative fallback so those notes can share
            // the same reviewer typography and natural pagination. Fixed tables
            // and forms remain native unless an aligned narrative is also present.
            var narrativeRows = rows.Where(row => row.Mono && !row.Columns && !row.Field &&
                row.Text.Count(char.IsLower) > 15 &&
                row.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 5 &&
                !Regex.IsMatch(row.Text, @"^(?:/es/|\d|[A-Z]\d)")).ToArray();
            if (narrativeRows.Length < 6)
                return null;
            anchor = narrativeRows.GroupBy(row => Math.Round(row.X))
                .OrderByDescending(group => group.Count()).FirstOrDefault()?
                .Select(row => row.X).ToArray();
            if (anchor is null || anchor.Length < 3) return null;
            var aligned = narrativeRows.Where(row => Math.Abs(row.X - anchor.Average()) < 1).ToHashSet();
            if (!rows.Zip(rows.Skip(1)).Any(pair => aligned.Contains(pair.First) && aligned.Contains(pair.Second) &&
                    pair.First.Page == pair.Second.Page && pair.First.Font == pair.Second.Font &&
                    Math.Abs(pair.First.Size - pair.Second.Size) < .1 &&
                    pair.Second.Baseline - pair.First.Baseline <= pair.First.Size * 1.8)) return null;
        }
        var indent = anchor.Average();
        var paragraphs = new List<string>();
        var current = "";
        Line? previous = null;
        var prose = false;
        var signature = false;
        void Flush() { if (current.Length > 0) paragraphs.Add(current); current = ""; }
        foreach (var row in rows)
        {
            // Labels and aligned multi-cell rows remain explicit boundaries.
            // Their sentence-like values can continue on subsequent baselines.
            var proseField = row.Mono && !row.Columns && row.Field && row.Text.Count(char.IsLower) > 18 &&
                row.Text.Split(' ').Length >= 5;
            var scalar = VeteransReviewerTextLayout.Classify(row.Text) == VeteransReviewerTextShape.DataRow ||
                Regex.IsMatch(row.Text, @"^[-=*_]{3,}$");
            var atIndent = !scalar && row.Mono && Math.Abs(row.X - indent) < 1 && !row.Columns &&
                (!row.Field || proseField);
            var marginFragment = row.Fragment && row.X < indent;
            var narrative = row.Mono && !row.Columns && !row.Field &&
                row.Text.Count(char.IsLower) > 15 && !Regex.IsMatch(row.Text, @"^(?:/es/|\d|[A-Z]\d)");
            var newItem = row.Text.Contains("/es/", StringComparison.Ordinal) ||
                Regex.IsMatch(row.Text, @"^(?:-\s|\d+[.)]\s|\d{4,},|[A-Z]\d+[.,])");
            var adjacent = previous is not null && (row.Page == previous.Page + 1 ||
                (row.Page == previous.Page && row.Baseline - previous.Baseline < Math.Max(row.Size, previous.Size) * 1.8));
            // A signature is a structured entry, not several independent
            // paragraphs when names or credentials wrap at the source margin.
            var signatureContinuation = signature && adjacent && row.Mono &&
                !row.Columns && !row.Field && row.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 4 &&
                !Regex.IsMatch(row.Text, @"^(?:\d|/es/|[-*])");
            var joins = signatureContinuation || (prose && adjacent && (!row.Field || char.IsLower(row.Text[0])) && !row.Columns && !scalar && !newItem &&
                (atIndent || marginFragment || (row.Field && char.IsLower(row.Text[0]) && previous is not null && Math.Abs(row.X - previous.X) < 1) || (narrative && previous is not null && (previous.Fragment || Math.Abs(row.X - previous.X) < 1))));
            if (!joins) Flush();
            if (current.Length > 0) current += " ";
            current += !row.Columns && (joins || atIndent || narrative || proseField || !row.Mono) ? row.Text : row.FixedText;
            signature = row.Text.Contains("/es/", StringComparison.Ordinal) || signatureContinuation;
            prose = !signature && !scalar && !row.Columns && (joins || atIndent || narrative || proseField);
            // Sentence completion is a natural paragraph boundary. A short
            // following fragment can start the next sentence without isolation.
            if (Regex.IsMatch(row.Text, "[.!?][\\\"')]*$") && !marginFragment)
            { Flush(); prose = false; }
            if (marginFragment && current == row.Text) prose = true;
            previous = row;
        }
        Flush();
        return patientHeaders.Concat(JoinDetachedPainScaleLines(JoinKnownFormLabels(paragraphs))).ToArray();
    }

    // Exact producer labels only; numeric rows and arbitrary short lines never
    // participate. Joining keeps every original word and value in order.
    internal static IReadOnlyList<string> JoinKnownFormLabels(IReadOnlyList<string> paragraphs)
    {
        string[] labels = ["STANDARD TITLE", "The patient was asked the following questions",
            "Post treatment Numeric Pain Rating Scale"];
        var result = new List<string>();
        for (var i = 0; i < paragraphs.Count; i++)
        {
            var text = paragraphs[i];
            foreach (var label in labels)
            {
                if (!label.StartsWith(text.Trim() + " ", StringComparison.OrdinalIgnoreCase)) continue;
                var joined = text.Trim();
                var end = i;
                while (end + 1 < paragraphs.Count && end - i < 5 &&
                       label.StartsWith(joined.TrimEnd(':') + " ", StringComparison.OrdinalIgnoreCase))
                {
                    joined += " " + paragraphs[++end].Trim();
                    if (joined.Equals(label, StringComparison.OrdinalIgnoreCase) ||
                        joined.StartsWith(label + ":", StringComparison.OrdinalIgnoreCase))
                    { text = joined; i = end; break; }
                }
            }
            result.Add(text);
        }
        return result;
    }

    internal static IReadOnlyList<string> JoinDetachedPainScaleLines(IReadOnlyList<string> paragraphs)
    {
        var output = new List<string>();
        for (var i = 0; i < paragraphs.Count; i++)
        {
            var current = paragraphs[i];
            if (output.Count > 0 && PainContext(output[^1]) && PainScaleLine(current))
            {
                output[^1] += " Pain " + current;
                if (i + 1 < paragraphs.Count && !PainScaleHasValue(current) &&
                    Regex.IsMatch(paragraphs[i + 1], @"^(?:10|[0-9])$"))
                    output[^1] += " " + paragraphs[++i];
                continue;
            }
            if (output.Count > 0 && PainContext(Regex.Replace(output[^1],
                    @"\s+(?:Pain )?number\s+from\s+0\s*-\s*10\s*:\s*$", "", RegexOptions.IgnoreCase)) &&
                Regex.IsMatch(output[^1], @"number\s+from\s+0\s*-\s*10\s*:\s*$", RegexOptions.IgnoreCase) &&
                Regex.IsMatch(current, @"^(?:10|[0-9])$"))
            {
                output[^1] += " " + current;
                continue;
            }
            current = Regex.Replace(current,
                @"(?<context>Numeric Pain Rating Scale[^.!?]*:|(?:^|(?<=[.!?])\s+)[^.!?]*\bpain\b[^.!?]*\?)\s+(?<scale>number\s+from\s+0\s*-\s*10\s*:)",
                "${context} Pain ${scale}", RegexOptions.IgnoreCase);
            output.Add(current);
        }
        return output;
    }

    private static bool PainContext(string text) => Regex.IsMatch(text,
        @"(?:Numeric Pain Rating Scale[^.!?]*:|(?:^|(?<=[.!?])\s+)[^.!?]*\bpain\b[^.!?]*\?)\s*$",
        RegexOptions.IgnoreCase);

    private static bool PainScaleLine(string text) => Regex.IsMatch(text,
        @"^number\s+from\s+0\s*-\s*10\s*:\s*(?:10|[0-9])?\s*$", RegexOptions.IgnoreCase);

    private static bool PainScaleHasValue(string text) => Regex.IsMatch(text,
        @":\s*(?:10|[0-9])\s*$");
}
