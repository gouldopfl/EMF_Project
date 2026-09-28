using System.Text.RegularExpressions;

namespace EMF.Extensions.VeteransClaims.Orchestration;

internal static class VeteransReviewerDateOfBirth
{
    private const string Label = @"\bDate of birth:[ \t]*";
    private const string Month = @"(?:January|February|March|April|May|June|July|August|September|October|November|December)";
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // Only a labelled month/day followed immediately by a standalone year.
    // Do not consume blank lines, another field, or arbitrary comma-ended prose.
    internal static string Reconstruct(string text)
    {
        // Flatten only the horizontal gap before the explicit patient DOB field.
        text = Regex.Replace(text, @"(?<=\S)[ \t]{2,}(?=Date of birth:)", " ", Options, TimeSpan.FromMilliseconds(100));
        text = Regex.Replace(text, Label + @"\r?\n[ \t]*(?=" + Month + @"[ \t]+\d{1,2},)",
            "Date of birth: ", Options, TimeSpan.FromMilliseconds(100));
        return Regex.Replace(text,
        @"(?<prefix>" + Label + Month + @"[ \t]+\d{1,2},)[ \t]*\r?\n[ \t]*(?<year>\d{4})(?=[ \t]*(?:\r?\n|$))",
        "${prefix} ${year}", Options, TimeSpan.FromMilliseconds(100));
    }

    // Nonbreaking spaces keep the entire value atomic, including comma/year.
    // Retain the label's ordinary spacing so the value can move to the next line.
    internal static string KeepTogether(string text) => Regex.Replace(text,
        @"(?<label>" + Label + @")(?<date>" + Month + @"[ \t\u00a0]+\d{1,2},[ \t\u00a0]+\d{4})\b",
        match => match.Groups["label"].Value + match.Groups["date"].Value.Replace(' ', '\u00a0').Replace('\t', '\u00a0'),
        Options, TimeSpan.FromMilliseconds(100));
}
