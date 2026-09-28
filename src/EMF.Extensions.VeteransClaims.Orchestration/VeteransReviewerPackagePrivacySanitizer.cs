using System.Text;
using System.Text.RegularExpressions;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public static class VeteransReviewerPackagePrivacySanitizer
{
    private static readonly Regex SensitiveIdentifierPattern =
        new(
            @"\b(?<label>" +
            @"VA\s+File\s+(?:Number|No\.?)|" +
            @"VA\s+Claim\s+(?:Number|No\.?)|" +
            @"Social\s+Security\s+(?:Number|No\.?)|" +
            @"SSN|MRN|Medical\s+Record\s+(?:Number|No\.?))" +
            @"(?<separator>\s*[:#]?\s*)" +
            @"(?<id>\d{3}(?:\s*[-–—]\s*|\s+)?\d{2}(?:\s*[-–—]\s*|\s+)?\d{4})\b",
            RegexOptions.IgnoreCase |
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));

    // A formatted SSN remains sensitive under MRN, in prose, or without a label.
    // Unlabelled contiguous nine-digit device / prescription numbers are not SSNs.
    private static readonly Regex FormattedSsnPattern = new(
        @"(?<![\d*])\b\d{3}(?:\s*[-–—]\s*|\s+)\d{2}(?:\s*[-–—]\s*|\s+)(?<last>\d{4})\b",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    internal sealed record Redaction(int Start, int Length, string Replacement);

    internal static IReadOnlyList<Redaction> Redactions(string text)
    {
        var spans = new List<Redaction>();
        foreach (Match match in SensitiveIdentifierPattern.Matches(text))
        {
            var id = match.Groups["id"];
            var digits = string.Concat(id.Value.Where(char.IsDigit));
            var prefix = match.Groups["label"].Value.StartsWith("VA", StringComparison.OrdinalIgnoreCase)
                ? "*****" : "***-**-";
            spans.Add(new(id.Index, id.Length, prefix + digits[^4..]));
        }
        foreach (Match match in FormattedSsnPattern.Matches(text))
            if (!spans.Any(s => match.Index < s.Start + s.Length && match.Index + match.Length > s.Start))
                spans.Add(new(match.Index, match.Length, "***-**-" + match.Groups["last"].Value));
        return spans.OrderBy(s => s.Start).ToArray();
    }

    public static string Redact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var output = new StringBuilder(text);
        foreach (var span in Redactions(text).Reverse())
            output.Remove(span.Start, span.Length).Insert(span.Start, span.Replacement);
        return output.ToString();
    }
}
