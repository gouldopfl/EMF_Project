using System.Text.RegularExpressions;

namespace EMF.Extensions.VeteransClaims.Orchestration;

internal sealed record VeteransReviewerTextCorrection(
    string RuleId, string Original, string Replacement, int ReviewerBlock, int Offset);

/// <summary>
/// Reviewer-only, exact lowercase whole-word rules. Uncertain tokens are left alone.
/// This is not a general spelling corrector or a clinical vocabulary normalizer.
/// </summary>
internal static class VeteransReviewerTypographicalCorrections
{
    private sealed record Rule(string Id, string Pattern, string Replacement);

    // Case-sensitive whole words: capitalized names and uppercase abbreviations
    // are never corrected. The caller restricts this to unprotected narrative.
    private static readonly Rule[] Rules =
    [
        new("examination-1", @"\bexaminaiton\b", "examination"),
        new("prolonged-1", @"\bprlonged\b", "prolonged"),
        new("persistence-1", @"\bpersisitence\b", "persistence"),
        new("which-1", @"\bwhic\b", "which"),
        new("veteran-1", @"\bvetern\b", "veteran"),
        new("and-1", @"\badn\b", "and")
    ];

    public static string Apply(VeteransReviewerTextBlock block,
        ICollection<VeteransReviewerTextCorrection> corrections)
    {
        IReadOnlyList<Rule> applicableRules = Rules;
        if (block.Shape == VeteransReviewerTextShape.Boundary)
        {
            // A wrapped source may strand a known misspelling on its own line.
            // Permit only an exact dictionary token with surrounding whitespace;
            // headings, statuses, lists, and other boundary text stay untouched.
            var token = block.Text.Trim();
            var rule = Rules.FirstOrDefault(candidate =>
            {
                var match = Regex.Match(token, candidate.Pattern,
                    RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                return match.Success && match.Index == 0 && match.Length == token.Length;
            });
            if (rule is null)
                return block.Text;
            applicableRules = [rule];
        }
        else if (block.Shape != VeteransReviewerTextShape.Narrative)
        {
            return block.Text;
        }

        // Narrative corrections retain the existing conservative content gate.
        // A standalone Boundary token above can contain no quote, value, label,
        // list marker, or extra identifier text by construction.
        if (block.Shape == VeteransReviewerTextShape.Narrative &&
            Regex.IsMatch(block.Text,
                @"[\d:""“”'‘’]|\b(?:medications?|diagnos\w*|prescri\w*|named|called|code|identifier|abbreviation)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            return block.Text;

        // Determine every edit against the same source string for stable offsets.
        var edits = applicableRules.SelectMany(rule =>
            Regex.Matches(block.Text, rule.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
                .Cast<Match>().Select(match => new VeteransReviewerTextCorrection(
                    rule.Id, match.Value, rule.Replacement, block.SourceLine, match.Index)))
            .OrderBy(edit => edit.Offset).ToArray();
        var output = block.Text;
        foreach (var edit in edits.Reverse())
            output = output.Remove(edit.Offset, edit.Original.Length).Insert(edit.Offset, edit.Replacement);
        foreach (var edit in edits)
            corrections.Add(edit);
        return output;
    }
}
