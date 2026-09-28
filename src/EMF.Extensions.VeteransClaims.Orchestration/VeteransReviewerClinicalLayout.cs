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

    private static readonly Regex NarrativeStart = Pattern(
        @"^(?:HPI|History of present illness|History of presenting illness)[ ]*:", true);
    private static readonly Regex CommaAcronym = Pattern(@"^[A-Z]{2,8},$");
    private static readonly Regex TrailingAcronym = Pattern(@"\b[A-Z]{2,8},$");
    private static readonly Regex ScaleHeading = Pattern(
        @"\b(?:questionnaire|rating scale|score sheet|assessment scale)\b", true);
    private static readonly Regex ScoreLine = Pattern(
        @"^(?<label>\p{L}[\p{L}\p{N} .'’()&/\-]{0,95}?:?)[ \t]+" +
        @"(?<score>\d{1,3}(?:\.\d+)?[ ]*/[ ]*(?<denominator>\d{1,3}(?:\.\d+)?))$");

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
