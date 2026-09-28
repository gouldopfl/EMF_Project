using System.Text.RegularExpressions;

namespace EMF.Extensions.VeteransClaims.Orchestration;

// A textual PowerForm export has no recoverable column coordinates. Preserve
// its order and field boundaries rather than assigning flattened values to
// invented cells. The renderer applies one readable font to the whole export.
internal static class VeteransReviewerPowerForm
{
    public static bool IsTextualRendition(string text) =>
        text.Contains("PowerForm Textual Rendition Notes", StringComparison.Ordinal);

    public static IReadOnlyList<string> Lines(string text)
    {
        // Only complete repeated producer headers are page furniture. Keep the
        // initial demographic/admission context; never treat a clinical field
        // or provider timestamp as a repeated table header.
        var seenHeader = false;
        text = Regex.Replace(text,
            @"(?m)^Patient Name:[^\r\n]*\r?\nMRN:[^\r\n]*\r?\nFIN:[^\r\n]*\r?\nSex/DOB/Age:[^\r\n]*\r?\n[^\r\n]*DOD ID \(EDIPI\):[^\r\n]*\r?\nPowerForm Textual Rendition Notes[ \t]*",
            match => { if (seenHeader) return ""; seenHeader = true; return match.Value; });
        text = Regex.Replace(text,
            @"(?m)^\d+[ \t]+Print Date/Time:[^\r\n]*Report Request ID:[ \t]*Page \d+ of \d+[ \t]*\r?$", "");
        // This exact split producer label is provable even without columns.
        text = Regex.Replace(text, @"(?m)^\*Progress Note/\r?\n[ \t]*Re-Eval Complete[ \t]*:",
            "*Progress Note/Re-Eval Complete:");
        var lines = VeteransReviewerPackagePrivacySanitizer.Redact(text)
            .Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var output = new List<string>();
        foreach (var source in lines)
        {
            var line = source.TrimEnd();
            // These are producer page-header labels with no remaining value,
            // not clinical observations. Keep any populated adjacent field.
            if (Regex.IsMatch(line.Trim(), @"^(?:Discharge|Admitting):\s*$")) continue;
            if (string.IsNullOrWhiteSpace(line))
            {
                if (output.Count > 0 && output[^1].Length > 0) output.Add("");
                continue;
            }
            output.Add(line);
        }
        while (output.Count > 0 && output[^1].Length == 0) output.RemoveAt(output.Count - 1);
        return output;
    }
}
