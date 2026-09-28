using System.Text.RegularExpressions;

namespace EMF.Extensions.VeteransClaims.Orchestration;

// Recognize complete source identity furniture, not arbitrary mentions of DOB.
// Names and dates are captured from the source; no patient-specific values.
internal static class VeteransReviewerPatientHeader
{
    private const string Name = @"[\p{L}\p{M}][\p{L}\p{M} '\u2019.-]*";
    private const string Month = @"(?:January|February|March|April|May|June|July|August|September|October|November|December)";
    private static readonly Regex Header = new(
        @"^(?:" + Name + @",[ \t]+" + Name + @"|Patient name:[ \t]*" + Name + @")[ \t]+Date of birth:[ \t]*" +
        @"(?:" + Month + @"[ \t]+\d{1,2},[ \t]+\d{4}|\d{1,2}/\d{1,2}/\d{4}|\d{4}-\d{2}-\d{2})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    internal static bool IsIdentity(string text) => Header.IsMatch(text.Trim().Replace('\u00a0', ' '));

    internal static string IdentityKey(string text) => Regex.Replace(text.Trim(), @"\s+", " ",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
}
