using System.Globalization;
using System.Text.RegularExpressions;

namespace EMF.Extensions.VeteransClaims.Orchestration;

// Plain-text encounter exports can carry their title/date in the opening source
// header rather than artifact metadata. Read that header without editing either.
internal static class VeteransReviewerEncounterHeader
{
    internal sealed record Header(string Title, string Date);

    internal static Header? Read(VeteransReviewerArtifactContent content)
    {
        if (content.Appendix != VeteransReviewerPackageAppendix.MedicalEvidence) return null;
        var lines = content.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Take(4).Select(line => line.Trim()).Where(line => line.Length > 0).Take(2).ToArray();
        if (lines.Length != 2 || lines[0].Length > 180) return null;
        var match = Regex.Match(lines[1], @"^Date entered:[ \t]*(?<date>[A-Za-z]+ \d{1,2}, \d{4})(?:,[ \t]*[^\r\n]+)?$",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return match.Success && DateOnly.TryParseExact(match.Groups["date"].Value, "MMMM d, yyyy",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? new(lines[0], date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) : null;
    }
}
