using System.Text.RegularExpressions;

namespace EMF.Extensions.VeteransClaims.Orchestration;

/// <summary>
/// Ephemeral layout recovery only. Source evidence, clinical values, and their
/// ordering are never modified. Uncertain structures retain the existing path.
/// </summary>
internal static class VeteransReviewerClinicalLayout
{
    internal sealed record PreparedText(string Text, IReadOnlySet<string> RejoinedNarratives);
    internal sealed record ScoreRow(string Label, string Score, string Denominator);

    internal static IReadOnlyDictionary<int, IReadOnlyList<string[]>> FindColumnBlocks(IReadOnlyList<string> lines)
    {
        var result = new Dictionary<int, IReadOnlyList<string[]>>();
        if (lines.Any(IsPowerForm)) return result;
        for (var start = 0; start < lines.Count; start++)
        {
            if (lines[start].Contains('\f')) continue;
            var header = Regex.Split(lines[start].Trim(), @"\t+|[ ]{2,}");
            if (header.Length is < 3 or > 8 ||
                !header.Any(c => Pattern(@"^(?:Result|Value|Units?|Reference(?: range)?|Date|Time)$", true).IsMatch(c)) ||
                !header.Any(c => Pattern(@"^(?:Test|Analyte|Component|Specimen|Study|Exam|Procedure|Date)$", true).IsMatch(c)))
                continue;
            var rows = new List<string[]> { header };
            var end = start + 1;
            while (end < lines.Count && rows.Count <= 128)
            {
                if (lines[end].Contains('\f') || lines[end].Trim().Length == 0 || lines[end].TrimEnd().EndsWith(':')) break;
                var cells = Regex.Split(lines[end].Trim(), @"\t+|[ ]{2,}");
                if (cells.Length != header.Length || cells.Any(c => c.Length == 0)) break;
                rows.Add(cells);
                end++;
            }
            // Two aligned data rows corroborate the explicit header. Do not
            // assign flattened scalar streams or ragged rows to guessed cells.
            if (rows.Count is >= 3 and <= 128) result.Add(start, rows);
            start = end - 1;
        }
        return result;
    }

    internal sealed record ColumnBlock(IReadOnlyList<string[]> Rows, int LineCount, bool HasHeader, IReadOnlyDictionary<int, IReadOnlyList<string>> UnassignedContinuations);
    internal sealed record RawRowBlock(IReadOnlyList<string> Lines)
    {
        internal int LineCount => Lines.Count;
    }

    // Holding adjacent source lines together changes pagination only. This path
    // deliberately retains raw spacing and line breaks when columns cannot be
    // demonstrated, including unpositioned scalar continuations.
    internal static IReadOnlyDictionary<int, RawRowBlock> FindAtomicLabRows(IReadOnlyList<string> lines)
    {
        var blocks = new Dictionary<int, RawRowBlock>();
        if (lines.Any(IsPowerForm)) return blocks;
        var inLab = false;
        var inComment = false;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line.Contains('\f') || string.IsNullOrWhiteSpace(line)) { inLab = inComment = false; continue; }
            if (Pattern(@"^(?:Collection DT|Collection Date)[ ]{2,}Specimen[ ]{2,}Test Name[ ]{2,}Result[ ]{2,}Units[ ]{2,}Ref(?:erence)?(?: Range)?$", true).IsMatch(line.TrimEnd()))
            {
                inLab = true;
                inComment = false;
                if (index + 1 < lines.Count && !lines[index + 1].Contains('\f') && lines[index + 1].Trim() == "Range") index++;
                continue;
            }
            if (!inLab) continue;
            if (Pattern(@"^[ ]*Comment:", true).IsMatch(line)) { inComment = true; continue; }
            if (!Pattern(@"^(?:\d{1,2}/\d{1,2}/\d{2,4}[ ]+\d{1,2}:\d{2}[ ]+\S|""[ ]+""[ ]+""[ ]{2,}\S)").IsMatch(line))
            {
                // An explicit comment remains on the ordinary text path. Its
                // indented wraps do not establish any data-cell relationships.
                if (inComment && line.StartsWith(' ') && !line.TrimEnd().EndsWith(':')) continue;
                inLab = false;
                continue;
            }
            inComment = false;
            var raw = new List<string> { line };
            var start = index;
            while (index + 1 < lines.Count && raw.Count <= 3)
            {
                var next = lines[index + 1];
                if (next.Contains('\f') || next.Contains('\t') ||
                    !Pattern(@"^(?:[-+<>≤≥=]?[\p{L}\p{N}./%+-]+|[-])$").IsMatch(next.Trim())) break;
                raw.Add(next);
                index++;
            }
            blocks.Add(start, new(raw));
        }
        return blocks;
    }

    // Fixed-width exports retain their column geometry even when empty units
    // prevent whitespace splitting. A complete explicit header anchors every cell.
    internal static IReadOnlyDictionary<int, ColumnBlock> FindLabBlocks(IReadOnlyList<string> lines)
    {
        var blocks = new Dictionary<int, ColumnBlock>();
        if (lines.Any(IsPowerForm)) return blocks;
        Match? activeHeader = null;
        for (var start = 0; start < lines.Count; start++)
        {
            if (lines[start].Contains('\f')) { activeHeader = null; continue; }
            if (lines[start].Trim().Length == 0 ||
                Pattern(@"^[A-Z /&-]+:$").IsMatch(lines[start].Trim())) activeHeader = null;
            var header = Pattern(@"^(?<date>Collection DT|Collection Date)[ ]{2,}(?<specimen>Specimen)[ ]{2,}(?<test>Test Name)[ ]{2,}(?<result>Result)[ ]{2,}(?<units>Units)[ ]{2,}(?<reference>Ref(?:erence)?(?: Range)?)$", true).Match(lines[start].TrimEnd());
            var hasHeader = header.Success;
            if (hasHeader) activeHeader = header;
            else if (activeHeader is not null && Pattern(@"^(?:\d{1,2}/\d{1,2}/\d{2,4}[ ]+\d{1,2}:\d{2}|"")").IsMatch(lines[start])) header = activeHeader;
            else continue;
            var names = new[] { "date", "specimen", "test", "result", "units", "reference" };
            var offsets = names.Select(n => Math.Max(0, header.Groups[n].Index - 1)).ToArray();
            offsets[0] = 0;
            var end = start + (hasHeader ? 1 : 0);
            var referenceHeader = header.Groups["reference"].Value;
            if (hasHeader && end < lines.Count && !lines[end].Contains('\f') && lines[end].Trim() == "Range") { referenceHeader += " Range"; end++; }
            var rows = new List<string[]> { names.Select(n => header.Groups[n].Value).ToArray() };
            rows[0][^1] = referenceHeader;
            if (!hasHeader) rows.Clear();
            var unassigned = new Dictionary<int, IReadOnlyList<string>>();
            while (end < lines.Count)
            {
                var line = lines[end];
                if (line.Contains('\f') || line.Contains('\t') ||
                    !Pattern(@"^(?:\d{1,2}/\d{1,2}/\d{2,4}[ ]+\d{1,2}:\d{2}|"")").IsMatch(line)) { activeHeader = null; break; }
                // A cut through ink is evidence that this header cannot safely
                // assign the row. Leave this and subsequent text on the original path.
                if (offsets.Skip(1).Any(o => o < line.Length && o > 0 &&
                    !char.IsWhiteSpace(line[o]) && !char.IsWhiteSpace(line[o - 1]))) { activeHeader = null; break; }
                var cells = offsets.Select((o, c) => o >= line.Length ? "" :
                    line[o..Math.Min(line.Length, c + 1 < offsets.Length ? offsets[c + 1] : line.Length)].Trim()).ToArray();
                if (cells[1].Length == 0 || cells[2].Length == 0 || cells[3].Length == 0) { activeHeader = null; break; }
                end++;
                // Positioned continuations may join the reference cell. An
                // unpositioned scalar remains a separate full-width source line;
                // keeping adjacent fragments on a page does not assign meaning.
                var fragments = new List<string>();
                var dangling = cells[^1].EndsWith(':') || cells[^1].EndsWith('-');
                for (var count = 0; end < lines.Count && count < 3; count++)
                {
                    var raw = lines[end];
                    var next = raw.Trim();
                    if (next.Length == 0 || raw.Contains('\f') || raw.Contains('\t') ||
                        !Pattern(@"^(?:[-+<>≤≥=]?[\p{L}\p{N}./%+-]+|[-])$").IsMatch(next)) break;
                    if (!(dangling || cells[^1].Length > 0 && next == "-")) break;
                    var positioned = raw.Length - raw.TrimStart(' ').Length >= offsets[^1];
                    if (positioned && fragments.Count == 0) cells[^1] += " " + next;
                    else fragments.Add(raw);
                    dangling = next == "-";
                    end++;
                }
                if (fragments.Count > 0) unassigned.Add(rows.Count, fragments);
                rows.Add(cells);
            }
            if (rows.Count > (hasHeader ? 1 : 0)) blocks.Add(start, new(rows, end - start, hasHeader, unassigned));
            start = Math.Max(start, end - 1);
        }
        return blocks;
    }

    private static readonly Regex NarrativeStart = Pattern(
        @"^(?:HPI|History of present illness|History of presenting illness)[ ]*:", true);
    private static readonly Regex CommaAcronym = Pattern(@"^[A-Z]{2,8},$");
    private static readonly Regex TrailingAcronym = Pattern(@"\b[A-Z]{2,8},$");
    private static readonly Regex ScaleHeading = Pattern(
        @"\b(?:questionnaire|rating scale|score sheet|assessment scale)\b", true);
    private static readonly Regex ScoreLine = Pattern(
        @"^(?<label>\p{L}[\p{L}\p{N} .'’()&/\-]{0,95}?:?)[ \t]+" +
        @"(?<score>\d{1,3}(?:\.\d+)?[ ]*/[ ]*(?<denominator>\d{1,3}(?:\.\d+)?))$");

    internal static string PrepareStructuredFields(string text, bool sourceIsPowerForm = false)
    {
        sourceIsPowerForm |= IsPowerForm(text);
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        // Preserve the page marker itself while making reconstruction on either
        // side independent. Trim operations must never erase this boundary.
        if (normalized.Contains('\f'))
            return string.Join("\f", normalized.Split('\f').Select(part =>
                PrepareStructuredFields(part, sourceIsPowerForm)));
        if (sourceIsPowerForm) return RecoverRecordTokens(normalized);
        normalized = Regex.Replace(normalized,
            @"(?m)^[ ]*(?<date>\d{1,2}/\d{1,2}/\d{4}|\d{4}-\d{2}-\d{2})[ ]*\n[ ]*(?<time>\d{1,2})[ ]*\n[ ]*:[ ]*\n[ ]*(?<minute>\d{2})(?=[ ]*$)",
            "${date} ${time}:${minute}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        // Only explicit known labels and demonstrated date/time token sequences
        // are recovered. These joins do not infer missing values or columns.
        foreach (var label in new[] { "The following points were placed", "Reason for Study", "Date entered",
                     "Date signed", "Date recorded", "Problem List", "Active Problems", "Onset Date", "Recorded Date" })
        {
            var parts = label.Split(' ');
            var pattern = @"(?m)^[ ]*" + string.Join(@"(?:[ ]|[ ]*\n[ ]*)", parts.Select(Regex.Escape)) + @"[ ]*:";
            normalized = Regex.Replace(normalized, pattern, label + ":", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
        }
        var lines = normalized.Split('\n').ToList();
        for (var index = 0; index < lines.Count; index++)
        {
            var match = Pattern(@"^(?<label>The following points were placed|Reason for Study|Date entered|Date signed|Date recorded):[ ]*(?<value>.*)$", true)
                .Match(lines[index].Trim());
            if (!match.Success || HasColumns(lines[index])) continue;
            var value = match.Groups["value"].Value;
            var count = 0;
            while (index + 1 < lines.Count && count++ < 12)
            {
                if (lines[index + 1].Contains('\f')) break;
                var next = lines[index + 1].Trim();
                if (next.Length == 0 || HasColumns(lines[index + 1]) ||
                    next.Contains(':') && !Pattern(@"^\d{1,2}[ ]*:[ ]*\d{2}(?:[ ]*:[ ]*\d{2})?$").IsMatch(next) &&
                    !Pattern(@"^(?:\d{1,2}/\d{1,2}/\d{4}|\d{4}-\d{2}-\d{2})[ ]+\d{1,2}:\d{2}(?::\d{2})?$").IsMatch(next) ||
                    next.EndsWith(':') || Pattern(@"^(?:[-=*_]{3,}|/es/|\d+[.)][ ]|[A-Z][A-Z /-]{3,}$)").IsMatch(next)) break;
                // With an existing value, only isolated tokens can demonstrate
                // a fragmented continuation. A following prose sentence stays separate.
                if (value.Length > 0 && WordCount(next) > 1) break;
                value = (value + " " + next).Trim();
                lines.RemoveAt(index + 1);
                if (EndsSentence(value) || WordCount(next) > 1) break;
            }
            if (match.Groups["label"].Value.StartsWith("Date", StringComparison.OrdinalIgnoreCase))
            {
                value = Regex.Replace(value, @"(?<=\d)[ ]*([/:\-])[ ]*(?=\d)", "$1", RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1));
                value = Regex.Replace(value, @"(?<=\d)[ ]+(AM|PM)\b", "\u00a0$1", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1));
            }
            lines[index] = match.Groups["label"].Value + ":" + (value.Length == 0 ? "" : " " + value);
        }
        // Standalone ISO/numeric date fragments inside problem-list entries are
        // joined only if the literal token order spells a complete date/time.
        normalized = string.Join("\n", lines);
        normalized = Regex.Replace(normalized, @"(?m)^[ ]*(Problem|Active)[ ]*\n[ ]*(List|Problems)[ ]*:?[ ]*$",
            "$1 $2", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        normalized = Regex.Replace(normalized, @"(?m)^[ ]*Date[ ]*\n[ ]*/[ ]*Time[ ]*:[ ]*\n(?<value>[^\n\f]+)$",
            match => match.Value.Split('\n').Any(HasColumns) || string.IsNullOrWhiteSpace(match.Groups["value"].Value)
                ? match.Value : "Date/Time: " + match.Groups["value"].Value.TrimStart(' '),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        normalized = Regex.Replace(normalized,
            @"(?m)^[ ]*(?<date>\d{1,2}/\d{1,2}/\d{4}|\d{4}-\d{2}-\d{2})[ ]*\n[ ]*(?<time>\d{1,2})[ ]*\n[ ]*:[ ]*\n[ ]*(?<minute>\d{2})(?=[ ]*$)",
            "${date} ${time}:${minute}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        normalized = RecoverRecordTokens(normalized);
        return normalized;
    }

    private static string RecoverRecordTokens(string text)
    {
        // Horizontal whitespace and single line breaks only: a blank line or
        // form feed is a hard boundary, including when a caller supplies pages.
        const string gap = @"[ ]?(?:\n[ ]?)?";
        const string zone = @"(?:UTC|GMT|EST|EDT|CST|CDT|MST|MDT|PST|PDT|AKST|AKDT|HST|HDT)";
        text = Regex.Replace(text,
            @"(?m)^(?<surname>[\p{L}.'’-]+(?:[ ][\p{L}.'’-]+)*,)" + gap +
            @"(?<provider>[\p{L}.'’-]+(?:[ ][\p{L}.'’-]+)*,[ ]?[A-Z]{2,8}(?:(?:[ ]|,[ ]?)[A-Z]{2,8})*)" + gap +
            @"-" + gap + @"(?<stamp>\d{1,2}/\d{1,2}/\d{2,4}[ ]\d{1,2}:\d{2}(?::\d{2})?)" + gap +
            @"(?<zone>" + zone + @")(?=[ ]*$)", "${surname} ${provider} - ${stamp}\u00a0${zone}",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        text = Regex.Replace(text, @"(?m)^(?<prefix>[^\n\f]*?)\(SNOMED" + gap + "CT" + gap + ":" + gap +
            @"(?<code>\d+)" + gap + @"\)(?<suffix>[^\n\f]*)$",
            match => match.Value.Split('\n').Any(HasColumns) ? match.Value :
                match.Groups["prefix"].Value + "(SNOMED CT :" + match.Groups["code"].Value + ")" + match.Groups["suffix"].Value,
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        // A complete semicolon-delimited record supplies its own relationships.
        // Do not rebuild a record interrupted by page furniture or another entry.
        var lines = text.Split('\n').ToList();
        for (var start = 0; start < lines.Count; start++)
        {
            if (!lines[start].TrimStart().StartsWith("Name of Problem:", StringComparison.OrdinalIgnoreCase)) continue;
            var end = start;
            while (end < lines.Count && end - start < 16 && lines[end].Trim().Length > 0 &&
                   !lines[end].Contains('\f') && !Pattern(@"^[-_]{3,}|SNOMED CT :|^Name of Problem:", true).IsMatch(end == start ? "" : lines[end].Trim()))
            {
                if (Pattern(@"Vocabulary:[ ]+SNOMED CT[ ]*$", true).IsMatch(lines[end])) break;
                end++;
            }
            if (end >= lines.Count || end - start >= 16 || lines[end].Contains('\f') ||
                !Pattern(@"Vocabulary:[ ]+SNOMED CT[ ]*$", true).IsMatch(lines[end])) continue;
            var record = string.Join(" ", lines.Skip(start).Take(end - start + 1).Select(l => l.Trim()));
            if (record.Contains('\t')) continue;
            record = Regex.Replace(record, @"(?<=:)[ ]+|(?<=;)[ ]+", " ");
            if (HasColumns(record)) continue;
            var fields = record.Split(';').Select(part => Pattern(@"^[ ]*(?<label>Name of Problem|Onset Date|Recorder|Confirmation|Classification|Code|Contributor System|Last Updated|Life Cycle Status|Responsible Provider|Vocabulary):[ ]*(?<value>.+)$", true).Match(part)).ToArray();
            if (fields.Any(f => !f.Success) || fields.Select(f => f.Groups["label"].Value).Distinct(StringComparer.OrdinalIgnoreCase).Count() != fields.Length) continue;
            var required = new[] { "Name of Problem", "Code", "Last Updated", "Life Cycle Status", "Vocabulary" };
            var labels = fields.Select(f => f.Groups["label"].Value).ToArray();
            var indices = required.Select(label => Array.FindIndex(labels, l => l.Equals(label, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (indices.Any(i => i < 0) || !indices.SequenceEqual(indices.OrderBy(i => i)) ||
                !Pattern(@"^\d+$").IsMatch(fields[indices[1]].Groups["value"].Value.Trim()) ||
                !Pattern(@"^\d{1,2}/\d{1,2}/\d{2,4}[ ]\d{1,2}:\d{2}(?::\d{2})?[ \u00a0]" + zone + "$").IsMatch(fields[indices[2]].Groups["value"].Value.Trim())) continue;
            record = Regex.Replace(record, @"(?<=\d{2}:\d{2})[ ](?=" + zone + @"\b)", "\u00a0");
            lines[start] = record;
            lines.RemoveRange(start + 1, end - start);
        }
        // Attach a split timezone only to a complete standalone timestamp or
        // explicit timestamp field. Semicolon problem records are handled above;
        // failed records and unknown uppercase words remain untouched.
        return Regex.Replace(string.Join("\n", lines),
            @"(?m)^(?<prefix>(?:[\p{L}][\p{L} /'-]*:[ ]?)?)(?<stamp>\d{1,2}/\d{1,2}/\d{2,4}[ ]\d{1,2}:\d{2}(?::\d{2})?)[ ]?\n[ ]?(?<zone>" + zone + @")(?=[ ]*$)",
            "${prefix}${stamp}\u00a0${zone}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    internal static PreparedText PrepareNarrative(string text)
    {
        var joined = new HashSet<string>(StringComparer.Ordinal);
        // A PowerForm export explicitly lacks the geometry needed to associate
        // columns. Never interpret it as ordinary prose or a score table here.
        if (IsPowerForm(text)) return new(text, joined);
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Split('\n').ToList();
        for (var start = 0; start < lines.Count; start++)
        {
            var opening = lines[start].Trim();
            var opener = NarrativeStart.Match(opening);
            if (!opener.Success || HasColumns(opening[opener.Length..])) continue;
            var prefix = opening;
            var acronymStart = start + 1;
            // Accommodate a few ordinary wrapped prefix lines, not an arbitrary
            // search through an encounter, another field, or another paragraph.
            while (acronymStart < lines.Count && acronymStart - start <= 6 &&
                   !CommaAcronym.IsMatch(lines[acronymStart].Trim()))
            {
                if (EndsSentence(prefix) || !IsProseContinuation(lines[acronymStart], 2)) break;
                prefix += " " + lines[acronymStart].Trim();
                acronymStart++;
            }
            if (acronymStart >= lines.Count || acronymStart - start > 6 ||
                WordCount(prefix) < 8 || !TrailingAcronym.IsMatch(prefix) ||
                !CommaAcronym.IsMatch(lines[acronymStart].Trim()) || HasColumns(lines[acronymStart]))
                continue;
            var continuation = acronymStart;
            while (continuation < lines.Count && continuation - acronymStart < 4 &&
                   CommaAcronym.IsMatch(lines[continuation].Trim()) && !HasColumns(lines[continuation]))
                continuation++;
            if (continuation >= lines.Count || !IsProseContinuation(lines[continuation], 5)) continue;

            var recovered = string.Join(" ", lines.Skip(start).Take(continuation - start + 1).Select(l => l.Trim()));
            // Only whitespace between demonstrated fragments changes. The set
            // carries the full-width presentation decision through classification.
            joined.Add(recovered);
            lines[start] = recovered;
            lines.RemoveRange(start + 1, continuation - start);
        }
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return new(joined.Count == 0 ? text : string.Join(newline, lines), joined);
    }

    internal static IReadOnlyDictionary<int, IReadOnlyList<ScoreRow>> FindScoreBlocks(IReadOnlyList<string> lines)
    {
        var result = new Dictionary<int, IReadOnlyList<ScoreRow>>();
        if (lines.Any(IsPowerForm)) return result;
        for (var start = 0; start < lines.Count; start++)
        {
            if (!TryScore(lines[start], out _) || !HasScaleContext(lines, start)) continue;
            var rows = new List<ScoreRow>();
            var end = start;
            while (end < lines.Count && rows.Count <= 64 && TryScore(lines[end], out var row))
            {
                rows.Add(row);
                end++;
            }
            // Repeated explicit label/score pairs, with one common scale. Never
            // turn a lone fraction, date, dosage, total, or mixed-scale list into
            // an inferred table. Do not calculate or "correct" any score.
            if (rows.Count is >= 3 and <= 64 &&
                rows.Select(r => r.Denominator).Distinct(StringComparer.Ordinal).Count() == 1)
                result.Add(start, rows);
            start = end - 1;
        }
        return result;
    }

    private static bool HasScaleContext(IReadOnlyList<string> lines, int start)
    {
        var blanks = 0;
        for (var index = start - 1; index >= 0 && index >= start - 4; index--)
        {
            var line = lines[index].Trim();
            if (line.Length == 0)
            {
                if (++blanks > 1) return false;
                continue;
            }
            if (TryScore(line, out _)) return false;
            if (line.Length <= 350 && ScaleHeading.IsMatch(line)) return true;
            // Only a directly preceding explanatory score line may separate a
            // heading and its rows; a different clinical section is a boundary.
            if (!Regex.IsMatch(line, @"^(?:total\b|score\b|higher\b|lower\b|disability\b)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                return false;
        }
        return false;
    }

    private static bool TryScore(string text, out ScoreRow row)
    {
        row = new("", "", "");
        var match = ScoreLine.Match(text.Trim());
        if (!match.Success) return false;
        var label = match.Groups["label"].Value.TrimEnd();
        if (Regex.IsMatch(label, @"\d[ ]*/[ ]*\d", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            return false;
        if (Regex.IsMatch(label, @"^(?:total|subtotal|overall)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            return false;
        row = new(label, match.Groups["score"].Value, match.Groups["denominator"].Value);
        return true;
    }

    private static bool IsProseContinuation(string text, int minimumWords)
    {
        var line = text.Trim();
        return line.Length > 0 && char.IsLower(line[0]) && WordCount(line) >= minimumWords &&
            !HasColumns(text) && !line.Contains(':') &&
            !Regex.IsMatch(line, @"[\[\]<>={}]", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    private static bool HasColumns(string text) => text.Contains('\t') || text.Contains('|') ||
        text.Trim().Contains("  ", StringComparison.Ordinal);
    private static bool EndsSentence(string text) => text.TrimEnd().EndsWith('.') ||
        text.TrimEnd().EndsWith('?') || text.TrimEnd().EndsWith('!') || text.TrimEnd().EndsWith(':');
    private static int WordCount(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    private static bool IsPowerForm(string text) => text.Contains("PowerForm", StringComparison.OrdinalIgnoreCase);
    private static Regex Pattern(string pattern, bool ignoreCase = false) => new(pattern,
        RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None), TimeSpan.FromSeconds(1));
}
