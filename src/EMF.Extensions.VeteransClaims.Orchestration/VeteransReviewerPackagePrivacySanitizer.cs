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
            @"SSN)" +
            @"(?<separator>\s*[:#]?\s*)" +
            @"(?<id>\d{3}[- ]?\d{2}[- ]?\d{4})\b",
            RegexOptions.IgnoreCase |
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));

    public static string Redact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return SensitiveIdentifierPattern.Replace(
            text,
            static match =>
            {
                var digits = new StringBuilder(9);

                foreach (var value in match.Groups["id"].Value)
                {
                    if (char.IsAsciiDigit(value))
                        digits.Append(value);
                }

                var identifier = digits.ToString();

                if (identifier.Length != 9)
                    return match.Value;

                return
                    match.Groups["label"].Value +
                    match.Groups["separator"].Value +
                    "*****" +
                    identifier[^4..];
            });
    }
}
