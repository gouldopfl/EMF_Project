using System.Globalization;
using System.Text.RegularExpressions;
using EMF.Core.Models;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed partial class VeteransBlueButtonMedicationLedgerParser
{
    private const string MedicationsHeading = "Medications";

    private const string MedicationsDescription =
        "This is a list of prescriptions and other medications in your VA medical records.";

    private const string FollowingSectionHeading =
        "My HealtheVet account summary";

    private const string PrescriptionHeading =
        "About your prescription";

    private static readonly string[] FieldPrefixes =
    [
        "Last filled on:",
        "Status:",
        "Refills left:",
        "Request refills by this prescription expiration date:",
        "Prescription number:",
        "Prescribed on:",
        "Prescribed by:",
        "Facility:",
        "Pharmacy phone number:",
        "Instructions:",
        "Reason for use:",
        "Quantity:"
    ];

    public VeteransBlueButtonMedicationLedgerParseResult Parse(
        IReadOnlyList<ExtractedArtifactTextPage> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        if (pages.Count == 0)
            throw new InvalidDataException(
                "Blue Button medication ledger contains no pages.");

        var lines = FlattenPages(pages);
        var sectionStart = FindMedicationSectionStart(lines);

        if (sectionStart < 0)
            throw new InvalidDataException(
                "Blue Button medications section was not found.");

        var sectionEnd =
            FindFollowingSection(lines, sectionStart);

        var reportedEntryCount =
            ParseReportedEntryCount(
                lines,
                sectionStart,
                sectionEnd);

        var reportDate =
            ParseReportDate(
                lines,
                sectionStart,
                sectionEnd);

        var prescriptionHeadings =
            Enumerable.Range(
                    sectionStart + 1,
                    sectionEnd - sectionStart - 1)
                .Where(index =>
                    string.Equals(
                        Normalize(lines[index].Text),
                        PrescriptionHeading,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();

        if (prescriptionHeadings.Length == 0)
            throw new InvalidDataException(
                "Blue Button medications section contained no prescription entries.");

        var entryStarts =
            prescriptionHeadings
                .Select(index =>
                    FindEntryStart(
                        lines,
                        sectionStart,
                        index))
                .ToArray();

        var entries =
            new List<VeteransBlueButtonMedicationLedgerEntry>(
                prescriptionHeadings.Length);

        for (var index = 0;
             index < prescriptionHeadings.Length;
             index++)
        {
            var start = entryStarts[index];
            var end =
                index + 1 < entryStarts.Length
                    ? entryStarts[index + 1] - 1
                    : sectionEnd - 1;

            end = FindLastMeaningfulLine(lines, start, end);

            if (end < start)
                throw new InvalidDataException(
                    "Blue Button medication entry was empty.");

            entries.Add(
                ParseEntry(
                    lines,
                    index + 1,
                    start,
                    end,
                    prescriptionHeadings[index]));
        }

        var finalLine =
            FindLastMeaningfulLine(
                lines,
                sectionStart,
                sectionEnd - 1);

        return new VeteransBlueButtonMedicationLedgerParseResult
        {
            ReportDate = reportDate,
            SourceStartPage = lines[sectionStart].PageNumber,
            SourceEndPage =
                finalLine >= sectionStart
                    ? lines[finalLine].PageNumber
                    : lines[sectionStart].PageNumber,
            ReportedEntryCount = reportedEntryCount,
            Entries = entries
        };
    }

    private static VeteransBlueButtonMedicationLedgerEntry ParseEntry(
        IReadOnlyList<PageLine> lines,
        int ordinal,
        int start,
        int end,
        int prescriptionHeading)
    {
        var medicationName =
            JoinMedicationName(
                lines,
                start,
                prescriptionHeading - 1);

        if (string.IsNullOrWhiteSpace(medicationName))
            throw new InvalidDataException(
                $"Blue Button medication entry {ordinal} has no medication name.");

        var status =
            ReadField(
                lines,
                prescriptionHeading + 1,
                end,
                "Status:");

        if (string.IsNullOrWhiteSpace(status))
            throw new InvalidDataException(
                $"Blue Button medication entry {ordinal} has no status.");

        var lastFilledText =
            ReadField(
                lines,
                prescriptionHeading + 1,
                end,
                "Last filled on:");

        var refillsText =
            ReadField(
                lines,
                prescriptionHeading + 1,
                end,
                "Refills left:");

        return new VeteransBlueButtonMedicationLedgerEntry
        {
            EntryOrdinal = ordinal,
            SourceStartPage = lines[start].PageNumber,
            SourceEndPage = lines[end].PageNumber,
            MedicationName = medicationName,
            Strength = ExtractStrength(medicationName),
            Status = status,
            PrescriptionNumber =
                ReadField(
                    lines,
                    prescriptionHeading + 1,
                    end,
                    "Prescription number:"),
            PrescribedDate =
                ParseOptionalDate(
                    ReadField(
                        lines,
                        prescriptionHeading + 1,
                        end,
                        "Prescribed on:")),
            LastFilledDate =
                ParseOptionalDate(lastFilledText),
            LastFilledOnText = lastFilledText,
            ExpirationDate =
                ParseOptionalDate(
                    ReadField(
                        lines,
                        prescriptionHeading + 1,
                        end,
                        "Request refills by this prescription expiration date:")),
            RefillsLeft = ParseOptionalNonNegativeInt(refillsText),
            Directions =
                ReadField(
                    lines,
                    prescriptionHeading + 1,
                    end,
                    "Instructions:"),
            Indication =
                ReadField(
                    lines,
                    prescriptionHeading + 1,
                    end,
                    "Reason for use:"),
            Prescriber =
                ReadField(
                    lines,
                    prescriptionHeading + 1,
                    end,
                    "Prescribed by:"),
            Facility =
                ReadField(
                    lines,
                    prescriptionHeading + 1,
                    end,
                    "Facility:"),
            Quantity =
                ReadField(
                    lines,
                    prescriptionHeading + 1,
                    end,
                    "Quantity:")
        };
    }

    private static IReadOnlyList<PageLine> FlattenPages(
        IReadOnlyList<ExtractedArtifactTextPage> pages)
    {
        var lines = new List<PageLine>();

        foreach (var page in pages)
        {
            if (page.PageNumber <= 0)
                throw new InvalidDataException(
                    "Blue Button page numbers must be positive.");

            foreach (var line in
                     page.Text
                         .Replace("\r\n", "\n", StringComparison.Ordinal)
                         .Replace('\r', '\n')
                         .Split('\n'))
            {
                lines.Add(
                    new PageLine(
                        page.PageNumber,
                        line));
            }
        }

        return lines;
    }

    private static int FindMedicationSectionStart(
        IReadOnlyList<PageLine> lines)
    {
        for (var index = lines.Count - 1;
             index >= 0;
             index--)
        {
            if (!string.Equals(
                    Normalize(lines[index].Text),
                    MedicationsHeading,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var maximum =
                Math.Min(
                    lines.Count,
                    index + 32);

            for (var probe = index + 1;
                 probe < maximum;
                 probe++)
            {
                if (string.Equals(
                        Normalize(lines[probe].Text),
                        MedicationsDescription,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private static int FindFollowingSection(
        IReadOnlyList<PageLine> lines,
        int sectionStart)
    {
        for (var index = sectionStart + 1;
             index < lines.Count;
             index++)
        {
            if (string.Equals(
                    Normalize(lines[index].Text),
                    FollowingSectionHeading,
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return lines.Count;
    }

    private static int? ParseReportedEntryCount(
        IReadOnlyList<PageLine> lines,
        int start,
        int end)
    {
        var maximum = Math.Min(end, start + 48);

        for (var index = start + 1;
             index < maximum;
             index++)
        {
            var match =
                ReportedCountRegex().Match(
                    Normalize(lines[index].Text));

            if (!match.Success)
                continue;

            if (int.TryParse(
                    match.Groups["count"].Value,
                    NumberStyles.Integer | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out var count) &&
                count >= 0)
            {
                return count;
            }
        }

        return null;
    }

    private static DateOnly ParseReportDate(
        IReadOnlyList<PageLine> lines,
        int start,
        int end)
    {
        for (var index = start;
             index < end;
             index++)
        {
            var match =
                ReportDateRegex().Match(
                    Normalize(lines[index].Text));

            if (!match.Success)
                continue;

            var value = match.Groups["date"].Value.Trim();

            if (DateOnly.TryParseExact(
                    value,
                    "MMMM d, yyyy",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date))
            {
                return date;
            }
        }

        throw new InvalidDataException(
            "Blue Button medications section report date was not found.");
    }

    private static int FindEntryStart(
        IReadOnlyList<PageLine> lines,
        int sectionStart,
        int prescriptionHeading)
    {
        var index = prescriptionHeading - 1;

        while (index > sectionStart &&
               (string.IsNullOrWhiteSpace(lines[index].Text) ||
                IsPageHeader(lines[index].Text)))
        {
            index--;
        }

        if (index <= sectionStart)
            throw new InvalidDataException(
                "Blue Button medication name was not found before prescription details.");

        var start = index;

        while (start - 1 > sectionStart)
        {
            var previousRaw = lines[start - 1].Text;
            var previous = Normalize(previousRaw);

            if (string.IsNullOrWhiteSpace(previous))
            {
                start--;
                continue;
            }

            if (IsPageHeader(previousRaw))
            {
                var candidate =
                    JoinMedicationName(
                        lines,
                        start,
                        prescriptionHeading - 1);

                if (HasUnmatchedClosingDelimiter(candidate))
                {
                    start--;
                    continue;
                }

                break;
            }

            if (IsSectionIntroduction(previous) ||
                IsMedicationEntryBoundary(previous) ||
                IsFieldPrefix(previous))
            {
                break;
            }

            start--;
        }

        return start;
    }

    private static string JoinMedicationName(
        IReadOnlyList<PageLine> lines,
        int start,
        int end) =>
        string.Join(
            " ",
            Enumerable.Range(start, end - start + 1)
                .Select(index => Normalize(lines[index].Text))
                .Where(value =>
                    !string.IsNullOrWhiteSpace(value) &&
                    !IsPageHeader(value)));

    private static string? ReadField(
        IReadOnlyList<PageLine> lines,
        int start,
        int end,
        string prefix)
    {
        for (var index = start;
             index <= end;
             index++)
        {
            var normalized = Normalize(lines[index].Text);

            if (!normalized.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = new List<string>();
            var first = normalized[prefix.Length..].Trim();

            if (!string.IsNullOrWhiteSpace(first))
                parts.Add(first);

            for (var continuation = index + 1;
                 continuation <= end;
                 continuation++)
            {
                var raw = lines[continuation].Text;
                var value = Normalize(raw);

                if (IsPageHeader(raw))
                    continue;

                if (string.IsNullOrWhiteSpace(value) ||
                    IsFieldPrefix(value) ||
                    string.Equals(
                        value,
                        "About this medication or supply",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        value,
                        PrescriptionHeading,
                        StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                parts.Add(value);
            }

            return parts.Count == 0
                ? null
                : string.Join(" ", parts);
        }

        return null;
    }

    private static DateOnly? ParseOptionalDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return DateOnly.TryParseExact(
                value.Trim(),
                "MMMM d, yyyy",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date)
            ? date
            : null;
    }

    private static int? ParseOptionalNonNegativeInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return int.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed) &&
            parsed >= 0
            ? parsed
            : null;
    }

    private static string? ExtractStrength(string medicationName)
    {
        var match = StrengthRegex().Match(medicationName);

        return match.Success
            ? match.Groups["strength"].Value.Trim()
            : null;
    }

    private static int FindLastMeaningfulLine(
        IReadOnlyList<PageLine> lines,
        int start,
        int end)
    {
        for (var index = end;
             index >= start;
             index--)
        {
            if (!string.IsNullOrWhiteSpace(lines[index].Text) &&
                !IsPageHeader(lines[index].Text))
            {
                return index;
            }
        }

        return start - 1;
    }


    private static bool HasUnmatchedClosingDelimiter(string value)
    {
        var parentheses = 0;
        var brackets = 0;

        foreach (var character in value)
        {
            switch (character)
            {
                case '(':
                    parentheses++;
                    break;
                case ')':
                    parentheses--;
                    if (parentheses < 0)
                        return true;
                    break;
                case '[':
                    brackets++;
                    break;
                case ']':
                    brackets--;
                    if (brackets < 0)
                        return true;
                    break;
            }
        }

        return false;
    }

    private static bool IsFieldPrefix(string value) =>
        FieldPrefixes.Any(prefix =>
            value.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase));

    private static bool IsMedicationEntryBoundary(string value) =>
        string.Equals(
            value,
            PrescriptionHeading,
            StringComparison.OrdinalIgnoreCase) ||
        string.Equals(
            value,
            "About this medication or supply",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsPageHeader(string value)
    {
        var normalized = Normalize(value);

        return normalized.StartsWith(
                   "Report generated by My HealtheVet on VA.gov on ",
                   StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains(
                   "Date of birth:",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSectionIntroduction(string value)
    {
        var normalized = Normalize(value);

        return string.Equals(
                   normalized,
                   MedicationsHeading,
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   normalized,
                   MedicationsDescription,
                   StringComparison.OrdinalIgnoreCase) ||
               ReportedCountRegex().IsMatch(normalized);
    }

    private static string Normalize(string value) =>
        value.Trim();

    [GeneratedRegex(
        @"^Showing\s+(?<count>[\d,]+)\s+medications\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReportedCountRegex();

    [GeneratedRegex(
        @"^Report generated by My HealtheVet on VA\.gov on\s+(?<date>.+?)\s+Page\s+\d+\s+of\s+\d+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReportDateRegex();

    [GeneratedRegex(
        @"\b(?<strength>\d+(?:\.\d+)?\s*(?:mcg|mg|g|ml|units?|%)(?:\s*/\s*(?:\d+(?:\.\d+)?\s*)?(?:mcg|mg|g|ml|hours?|hour|hr))?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StrengthRegex();

    private sealed record PageLine(
        int PageNumber,
        string Text);
}
