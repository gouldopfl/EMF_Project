using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models;
using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed record VeteransReviewerSourceCheckFinding(
    ArtifactId ArtifactId, int? SourcePage, string Rule, string Value,
    string SourceText, string Message);

/// <summary>
/// Deterministic source-data checks, not medical interpretation or corrections.
/// Native selected-page text takes precedence over potentially stale extraction.
/// Findings identify values requiring human review; no replacement is inferred.
/// </summary>
public static class VeteransReviewerSourceReasonablenessReview
{
    private static readonly Regex Weight = Pattern(
        @"\b(?:weight\s+(?:loss|gain)|lost|gained|weighs?|wt|(?:body\s+)?weight)\b[^\d.!?;]{0,45}(?<number>\d+(?:,\d{3})*(?:\.\d+)?)\s*(?<unit>pounds?|lbs?\.?|kilograms?|kg)\b");
    private static readonly Regex Percentage = Pattern(
        @"\b(?:SpO2|oxygen\s+saturation|(?:PAP\s+)?(?:adherence|compliance)(?:\s+rate)?|mask\s+fit)\s*(?:[:=]|(?:is|of|was|at)\b)?\s*(?<number>-?\d+(?:\.\d+)?)\s*%");
    private const string Month = @"(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:tember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)";
    private static readonly Regex Date = Pattern(
        @"(?<![\d/.-])(?:\d{4}-\d{1,2}-\d{1,2}|\d{1,2}/\d{1,2}/\d{4}|" + Month + @"\s+\d{1,2},?\s+\d{4})(?!\d|/)");
    private static readonly Regex CompletedDate = Pattern(
        @"\b(?:date\s+of\s+note|note\s+dated|entry\s+date|date\s+(?:signed|entered)|signed|DOB|date\s+of\s+birth|born|performed|completed|admitted|discharged|collected|resulted|diagnosed|was\s+seen)\b");
    private static readonly Regex PlannedDate = Pattern(
        @"\b(?:appointment|scheduled|follow[ -]?up|next\s+visit|return\s+(?:on|in)|planned|plan|will|to\s+be|due|expires?|expiration|valid\s+through)\b");
    private static readonly Regex ProspectiveAction = Pattern(
        @"\b(?:will|to\s+be)\b[^.;\r\n]{0,50}\b(?:signed|performed|completed|admitted|discharged|collected)\b");
    private static readonly Regex PlannedRecord = Pattern(
        @"\b(?:scheduled|planned|appointment reminder|future appointment)\b");
    private static readonly string[] DateFormats =
        ["yyyy-M-d", "M/d/yyyy", "MMM d, yyyy", "MMMM d, yyyy", "MMM d yyyy", "MMMM d yyyy", "MMM d,yyyy", "MMMM d,yyyy"];

    public static IReadOnlyList<VeteransReviewerSourceCheckFinding> Review(
        VeteransReviewerPackageDetails details, DateOnly reviewDate)
    {
        ArgumentNullException.ThrowIfNull(details);
        var members = details.PackageDetails.Artifacts
            .Where(a => a.ContentRole == EvidencePackageContentRoles.UnderlyingEvidence)
            .ToDictionary(a => a.ArtifactId);
        return details.ArtifactContents.Where(c => members.ContainsKey(c.Artifact.Id))
            .SelectMany(c => Review(c, members[c.Artifact.Id].ReviewerPageSelection is { } selection
                ? VeteransReviewerPageSelector.Select(c.PrintablePages, selection) : c.PrintablePages, reviewDate))
            .ToArray();
    }

    internal static IReadOnlyList<VeteransReviewerSourceCheckFinding> Review(
        VeteransReviewerArtifactContent content, IReadOnlyList<PrintableArtifactPage> pages, DateOnly reviewDate)
    {
        if (content.Appendix is not (VeteransReviewerPackageAppendix.MedicalEvidence or
                VeteransReviewerPackageAppendix.MedicalOpinionEvidence) &&
            content.Artifact.ArtifactType != "veterans-clinical-note") return [];
        var findings = new List<VeteransReviewerSourceCheckFinding>();
        foreach (var key in new[] { VeteransArtifactMetadataKeys.NoteDate, VeteransArtifactMetadataKeys.EvidenceDate })
        {
            if (!content.Artifact.Metadata.TryGetValue(key, out var value) || value is null) continue;
            var title = content.Artifact.Metadata.GetValueOrDefault(VeteransArtifactMetadataKeys.EvidenceTitle)?.ToString()
                ?? content.Artifact.Name;
            Check((PlannedRecord.IsMatch(title) && !CompletedDate.IsMatch(title)
                ? "Scheduled event: " : "Date of note: ") + value, null);
        }
        if (pages.Count == 0) Check(content.Text, null);
        else foreach (var page in pages)
        {
            if (page.TextGeometry is { } geometry) Check(NativeText(geometry), page.PageNumber);
            else if (page.ContentType == "text/plain") Check(Encoding.UTF8.GetString(page.Content.Span), page.PageNumber);
            // Do not OCR, or attribute full-artifact extraction to an image-only
            // selected page whose native text is unavailable.
        }
        return findings.Distinct().ToArray();

        void Check(string text, int? page)
        {
            foreach (Match match in Weight.Matches(text))
            {
                var parsed = decimal.TryParse(match.Groups["number"].Value,
                    NumberStyles.Number, CultureInfo.InvariantCulture, out var number);
                var kilograms = match.Groups["unit"].Value.StartsWith("k", StringComparison.OrdinalIgnoreCase);
                // Intentionally broad review thresholds, not normal clinical
                // ranges or proof of the intended value. Avoid routine outliers.
                if (parsed && number <= (kilograms ? 1000 : 2000)) continue;
                Add("ExtremeWeightMagnitude", match, match.Groups["number"].Value + " " + match.Groups["unit"].Value,
                    "Implausible human weight magnitude. Verify the source value, unit, and decimal placement before relying on it; no corrected value has been inferred.");
            }
            foreach (Match match in Percentage.Matches(text))
            {
                if (decimal.TryParse(match.Groups["number"].Value, NumberStyles.Number,
                        CultureInfo.InvariantCulture, out var number) && number is >= 0 and <= 100) continue;
                Add("PercentageOutsideBounds", match, match.Groups["number"].Value + "%",
                    "This explicitly bounded percentage is outside 0–100%. Verify the source value and its meaning before relying on it.");
            }
            var previousDateEnd = 0;
            foreach (Match match in Date.Matches(text))
            {
                var start = Math.Max(previousDateEnd, Math.Max(0, match.Index - 120));
                var preceding = text[start..match.Index];
                // A date's own clause controls the appointment exception. An
                // appointment elsewhere must not excuse a future signature.
                var boundary = preceding.LastIndexOfAny([';', '.', '\n', '\r']);
                if (boundary >= 0 && !string.IsNullOrWhiteSpace(preceding[(boundary + 1)..]))
                    preceding = preceding[(boundary + 1)..];
                previousDateEnd = match.Index + match.Length;
                if (!DateOnly.TryParseExact(match.Value, DateFormats, CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces, out var date))
                {
                    Add("InvalidCalendarDate", match, match.Value,
                        "This date is not a valid calendar date. Verify the original record; no replacement date has been inferred.");
                    continue;
                }
                if (date <= reviewDate) continue;
                var completed = CompletedDate.IsMatch(preceding) && !ProspectiveAction.IsMatch(preceding);
                if (!completed && PlannedDate.IsMatch(preceding)) continue;
                Add("FutureRecordedDate", match, match.Value,
                    $"This {(completed ? "documented-event" : "source")} date is after the review date (" +
                    reviewDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ")" +
                    " and is not clearly a scheduled or planned event. Verify the date and context before relying on it.");
            }

            void Add(string rule, Match match, string value, string message) => findings.Add(new(
                content.Artifact.Id, page, rule, value, Excerpt(text, match), message));
        }
    }

    private static Regex Pattern(string pattern) => new(pattern,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static string Excerpt(string text, Match match)
    {
        var start = text.LastIndexOf('\n', Math.Max(0, match.Index - 1));
        var end = text.IndexOf('\n', match.Index + match.Length);
        return text[(start < 0 ? 0 : start + 1)..(end < 0 ? text.Length : end)].Trim();
    }

    private static string NativeText(PrintableArtifactTextGeometry geometry)
    {
        var text = new StringBuilder();
        foreach (var row in geometry.Glyphs.GroupBy(g => Math.Round(g.Baseline, 1)).OrderBy(g => g.Key))
        {
            PrintableArtifactGlyph? previous = null;
            foreach (var glyph in row.OrderBy(g => g.X))
            {
                if (previous is not null && glyph.X - previous.EndX > glyph.FontSize * .15 &&
                    text.Length > 0 && !char.IsWhiteSpace(text[^1])) text.Append(' ');
                text.Append(glyph.Text);
                previous = glyph;
            }
            text.AppendLine();
        }
        return text.ToString();
    }
}
