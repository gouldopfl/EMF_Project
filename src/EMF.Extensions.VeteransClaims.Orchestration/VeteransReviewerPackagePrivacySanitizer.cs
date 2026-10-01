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
            @"(?!\w)(?<separator>\s*[:#]?\s*)" +
            @"(?<id>\d{3}(?:\s*[-–—]\s*|\s+)?\d{2}(?:\s*[-–—]\s*|\s+)?\d{4})\b",
            RegexOptions.IgnoreCase |
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));


    // Reviewer copies do not need full encounter / federal patient identifiers.
    // Suppress internal identifiers. SSN-shaped MRNs retain the specifically
    // permitted last-four presentation in SensitiveIdentifierPattern.
    private static readonly Regex OtherPatientIdentifierPattern = new(
        @"\b(?<label>MRN|FIN|DOD\s+ID\s*\(EDIPI\)|Veterans?\s+ID\s*\(ICN\))" +
        @"(?!\w)(?<separator>(?:\s*[:#]\s*|\s+))" +
        @"(?<id>\d{10}V\d{6}|\d{6,})(?=\b|DOD\s+ID|Veterans?\s+ID)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    // ICNs can be emitted ahead of their label by fixed-layout extraction. The
    // 10-digit + V + 6-digit shape is specific enough to mask conservatively
    // even when the label and value are separated by source geometry.
    private static readonly Regex UnlabelledIcnPattern = new(
        @"\b(?<id>\d{10}V\d{6})(?:Veterans?\s+ID\s*\(ICN\)\s*:|(?=\s|$))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));


    private static readonly Regex NeutralPatientIdentifierLine = new(
        @"^\s*Patient\s+identifier\s*:\s*(?<last>[A-Z0-9]{4})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex MaskedStandalonePatientIdentifierLine = new(
        @"^\s*(?:SSN|MRN|Social\s+Security\s+(?:Number|No\.?)|Medical\s+Record\s+(?:Number|No\.?))" +
        @"\s*[:#]?\s*\*{3}-\*{2}-(?<last>\d{4})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
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
        // PowerForm extraction can put EDIPI before the ICN label and the EDIPI
        // label after the ICN value. Suppress only this complete, labelled shape.
        foreach (Match match in Regex.Matches(text,
            @"\b\d{10}[ \t]+Veterans?\s+ID\s*\(ICN\)\s*:\s*\d{10}V\d{6}\s*DOD\s+ID\s*\(EDIPI\)\s*:",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            spans.Add(new(match.Index, match.Length, ""));
        foreach (Match match in SensitiveIdentifierPattern.Matches(text))
        {
            var id = match.Groups["id"];
            var digits = string.Concat(id.Value.Where(char.IsDigit));
            var prefix = match.Groups["label"].Value.StartsWith("VA", StringComparison.OrdinalIgnoreCase)
                ? "*****" : "***-**-";
            spans.Add(new(id.Index, id.Length, prefix + digits[^4..]));
        }
        foreach (Match match in OtherPatientIdentifierPattern.Matches(text))
        {
            if (spans.Any(s => match.Index < s.Start + s.Length && match.Index + match.Length > s.Start))
                continue;
            spans.Add(new(match.Index, match.Length, ""));
        }
        foreach (Match match in UnlabelledIcnPattern.Matches(text))
        {
            if (spans.Any(s => match.Index < s.Start + s.Length && match.Index + match.Length > s.Start))
                continue;
            spans.Add(new(match.Index, match.Length, ""));
        }
        foreach (Match match in FormattedSsnPattern.Matches(text))
            if (!spans.Any(s => match.Index < s.Start + s.Length && match.Index + match.Length > s.Start))
                spans.Add(new(match.Index, match.Length, "***-**-" + match.Groups["last"].Value));
        return spans.OrderBy(s => s.Start).ToArray();
    }


    internal static bool TryNeutralizeStandalonePatientIdentifier(string text, out string neutral)
    {
        ArgumentNullException.ThrowIfNull(text);
        var redacted = Redact(text);
        var neutralMatch = NeutralPatientIdentifierLine.Match(redacted);
        if (neutralMatch.Success)
        {
            neutral = "Patient identifier: " + neutralMatch.Groups["last"].Value;
            return true;
        }
        var maskedMatch = MaskedStandalonePatientIdentifierLine.Match(redacted);
        if (maskedMatch.Success)
        {
            neutral = "Patient identifier: " + maskedMatch.Groups["last"].Value;
            return true;
        }
        neutral = string.Empty;
        return false;
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
