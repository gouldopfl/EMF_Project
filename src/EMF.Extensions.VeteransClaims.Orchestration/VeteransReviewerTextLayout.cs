using System.Text.RegularExpressions;

namespace EMF.Extensions.VeteransClaims.Orchestration;

internal enum VeteransReviewerTextShape { Blank, Narrative, Field, Preformatted, Boundary }

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
        return blocks;
    }

    public static VeteransReviewerTextShape Classify(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return VeteransReviewerTextShape.Blank;
        if (IsPreformatted(line))
            return VeteransReviewerTextShape.Preformatted;
        if (Match(line, @"^\s*[\p{L}][\p{L} /()'-]{0,70}:"))
            return VeteransReviewerTextShape.Field;
        // Lists, headings, identifiers and one-word status lines are boundaries.
        if (Match(line, @"^\s*(?:[-*•]|\d+[.)])\s") ||
            !Match(line, @"\p{Ll}") || line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 5)
            return VeteransReviewerTextShape.Boundary;
        return VeteransReviewerTextShape.Narrative;
    }

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
