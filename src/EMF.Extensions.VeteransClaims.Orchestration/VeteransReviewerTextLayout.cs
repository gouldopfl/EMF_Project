using System.Text.RegularExpressions;

namespace EMF.Extensions.VeteransClaims.Orchestration;

internal enum VeteransReviewerTextShape { Blank, Narrative, Field, Preformatted, Boundary, DataRow }

internal sealed record VeteransReviewerTextBlock(
    string Text, VeteransReviewerTextShape Shape, int SourceLine);

/// <summary>Conservative layout classification; does not infer missing table cells.</summary>
internal static class VeteransReviewerTextLayout
{
    public static IReadOnlyList<VeteransReviewerTextBlock> Project(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var blocks = new List<VeteransReviewerTextBlock>();
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var shape = Classify(line);
            if (shape == VeteransReviewerTextShape.Narrative && blocks.Count > 0 &&
                blocks[^1].Shape == shape && CanJoinWrap(blocks[^1].Text, line))
                blocks[^1] = blocks[^1] with { Text = blocks[^1].Text + " " + line };
            else
                blocks.Add(new(line, shape, index + 1));
        }
        PreserveFragmentedStructure(blocks);
        PreserveAnchoredDataRows(blocks);
        PreserveRepeatedEmptyLabels(blocks);
        return blocks;
    }

    private static void PreserveAnchoredDataRows(List<VeteransReviewerTextBlock> blocks)
    {
        var inData = false;
        for (var index = 0; index < blocks.Count; index++)
        {
            var block = blocks[index];
            if (IsExplicitDataRow(block.Text))
            {
                inData = true;
                continue;
            }
            // Only isolated continuation tokens inherit this typography. Fields,
            // headings, prose and blank lines remain independent boundaries.
            if (EndsFragmentedRegion(block) || block.Shape == VeteransReviewerTextShape.Field)
                inData = false;
            else if (inData && block.Shape == VeteransReviewerTextShape.Boundary &&
                !block.Text.Trim().Any(char.IsWhiteSpace))
                blocks[index] = block with { Shape = VeteransReviewerTextShape.DataRow };
        }
    }

    private static void PreserveRepeatedEmptyLabels(List<VeteransReviewerTextBlock> blocks)
    {
        // Repeated empty column labels can survive a flattened table even when
        // column coordinates do not. Style only that exact repetition; a lone
        // empty label still stops fragment detection and begins a new section.
        for (var start = 0; start < blocks.Count; start++)
        {
            var label = blocks[start].Text.Trim();
            if (blocks[start].Shape != VeteransReviewerTextShape.Field ||
                label.IndexOf(':') != label.Length - 1)
                continue;
            var end = start + 1;
            while (end < blocks.Count && blocks[end].Shape == VeteransReviewerTextShape.Field &&
                blocks[end].Text.Trim() == label)
                end++;
            if (end - start >= 3)
                for (var index = start; index < end; index++)
                    blocks[index] = blocks[index] with { Shape = VeteransReviewerTextShape.DataRow };
            start = end - 1;
        }
    }

    private static void PreserveFragmentedStructure(List<VeteransReviewerTextBlock> blocks)
    {
        // A fragment must be demonstrated locally, not accumulated across
        // unrelated headings/fields. Never propagate formatting backwards from
        // a later fragment or across an explicit section/paragraph boundary.
        for (var start = 0; start < blocks.Count;)
        {
            if (EndsFragmentedRegion(blocks[start]))
            {
                start++;
                continue;
            }
            var end = start;
            var consecutiveTokens = 0;
            int? fragmentStart = null;
            while (end < blocks.Count && !EndsFragmentedRegion(blocks[end]))
            {
                var block = blocks[end];
                var isolatedToken = (block.Shape is VeteransReviewerTextShape.Boundary or VeteransReviewerTextShape.Preformatted or VeteransReviewerTextShape.DataRow) &&
                    !block.Text.Trim().Any(char.IsWhiteSpace);
                consecutiveTokens = isolatedToken ? consecutiveTokens + 1 : 0;
                if (consecutiveTokens == 4 && fragmentStart is null)
                    fragmentStart = end - 3;
                end++;
            }
            if (fragmentStart is int first)
                for (var index = first; index < end; index++)
                    blocks[index] = blocks[index] with { Shape = VeteransReviewerTextShape.Preformatted };
            start = end;
        }
    }

    private static bool EndsFragmentedRegion(VeteransReviewerTextBlock block)
    {
        if (block.Shape is VeteransReviewerTextShape.Narrative or VeteransReviewerTextShape.Blank)
            return true;
        var text = block.Text.Trim();
        if (Match(text, @"^[-=*_]{3,}$"))
            return true;
        if (block.Shape == VeteransReviewerTextShape.Preformatted)
            return false;
        // A label without a value, explicit section numbering, heading markup,
        // divider, or multiword structural line is a conservative stopping point. A
        // column header with explicit spacing remains preformatted on its own.
        return text.EndsWith(':') ||
            Match(text, @"^(?:#{1,6}\s|(?:\d+|[A-Za-z])[.)]\s+\p{L})") ||
            (block.Shape is VeteransReviewerTextShape.Boundary or VeteransReviewerTextShape.DataRow) && text.Any(char.IsWhiteSpace);
    }

    public static VeteransReviewerTextShape Classify(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return VeteransReviewerTextShape.Blank;
        if (IsPreformatted(line))
            return VeteransReviewerTextShape.Preformatted;
        if (IsDataRow(line))
            return VeteransReviewerTextShape.DataRow;
        if (Match(line, @"^\s*[\p{L}][\p{L} /()'-]{0,70}:"))
            return VeteransReviewerTextShape.Field;
        // Lists, headings, identifiers and one-word status lines are boundaries.
        if (Match(line, @"^\s*(?:[-*•]|\d+[.)])\s") ||
            Match(line.Trim(), @"^(?:\p{Lu}\p{L}* +){2,}\p{Lu}\p{L}*$") ||
            !Match(line, @"\p{Ll}") || line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 5)
            return VeteransReviewerTextShape.Boundary;
        return VeteransReviewerTextShape.Narrative;
    }

    private static bool IsDataRow(string line) =>
        // Numeric data and explicit ditto columns remain data even when a
        // paragraph boundary separates them from a longer fragmented run.
        // Require the whole row: numbered headings and quoted prose do not qualify.
        Match(line.Trim(), @"^(?=.*\d)[\d .,/:+%<>=()\-]+$") ||
        IsExplicitDataRow(line);

    private static bool IsExplicitDataRow(string line) =>
        Match(line.Trim(), @"^\d{1,2}/\d{1,2}/\d{4} +\d{1,2}:\d{2} +[\p{Lu}\d ()/_-]+$") ||
        Match(line.Trim(), "^(?:\" +){2,}\"(?: +[^\"]*)?$");

    public static bool IsPreformatted(string line) =>
        line.Contains('\t') ||
        Match(line.Trim(), @"\S[ ]{3,}\S.*[ ]{3,}\S") ||
        Match(line, @"\|.*\|") ||
        Match(line, @"\[[ xX]?\]|_{3,}|\d[ ]{2,}\S.*[ ]{2,}\d");

    private static bool CanJoinWrap(string previous, string next) =>
        // Only an unfinished prose line followed by a lower-case continuation.
        // Preserve indents, doubled spaces, measurements, codes and hyphenation.
        previous == previous.Trim() && next == next.Trim() &&
        !previous.Contains("  ", StringComparison.Ordinal) && !next.Contains("  ", StringComparison.Ordinal) &&
        !Match(previous + next, @"[\d\t:""“”]") &&
        Match(previous, @"[\p{Ll},]$") && Match(next, @"^\p{Ll}") &&
        previous.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 6;

    private static bool Match(string text, string pattern) =>
        Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
}
