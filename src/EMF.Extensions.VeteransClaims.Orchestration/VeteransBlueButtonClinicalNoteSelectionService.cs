using System.Globalization;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransBlueButtonClinicalNoteSelectionService
{
    public VeteransBlueButtonCareSummaryRecord Select(
        IReadOnlyList<VeteransBlueButtonCareSummaryRecord> records,
        DateOnly noteDate,
        string noteTitle)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentException.ThrowIfNullOrWhiteSpace(noteTitle);

        var normalizedTitle = noteTitle.Trim();

        var matches =
            records
                .Where(record =>
                    MatchesDate(record, noteDate) &&
                    MatchesTitle(record, normalizedTitle))
                .ToArray();

        if (matches.Length == 0)
        {
            var sameDate =
                records
                    .Where(record => MatchesDate(record, noteDate))
                    .Take(5)
                    .Select(Describe)
                    .ToArray();

            var sameTitle =
                records
                    .Where(record => MatchesTitle(record, normalizedTitle))
                    .Take(5)
                    .Select(Describe)
                    .ToArray();

            var nearestDate =
                records
                    .Select(record =>
                        (Record: record, Date: TryParseDate(record)))
                    .Where(item => item.Date.HasValue)
                    .OrderBy(item =>
                        Math.Abs(
                            item.Date!.Value.DayNumber -
                            noteDate.DayNumber))
                    .ThenBy(item => item.Date)
                    .Take(5)
                    .Select(item => Describe(item.Record))
                    .ToArray();

            var textTitle =
                records
                    .Where(record =>
                        record.Text.Contains(
                            normalizedTitle,
                            StringComparison.OrdinalIgnoreCase))
                    .Take(5)
                    .Select(Describe)
                    .ToArray();

            throw new InvalidDataException(
                $"No Blue Button care-summary record matched " +
                $"{noteDate:yyyy-MM-dd} / '{normalizedTitle}'. " +
                $"Parsed records: {records.Count}. " +
                $"Parsable date span: {DescribeDateSpan(records)}. " +
                $"Same-date candidates: {DescribeCandidates(sameDate)}. " +
                $"Same-title candidates: {DescribeCandidates(sameTitle)}. " +
                $"Nearest-date candidates: {DescribeCandidates(nearestDate)}. " +
                $"Text-title candidates: {DescribeCandidates(textTitle)}. " +
                $"Boundary candidates: {DescribeBoundaryCandidates(records)}.");
        }

        if (matches.Length > 1)
        {
            throw new InvalidDataException(
                $"Blue Button care-summary record selection is ambiguous " +
                $"for {noteDate:yyyy-MM-dd} / '{normalizedTitle}'; " +
                $"{matches.Length} records matched.");
        }

        return matches[0];
    }

    private static string Describe(
        VeteransBlueButtonCareSummaryRecord record) =>
        $"'{record.DateEntered}' / '{record.Title}' / pages " +
        $"{record.SourceStartPage}-{record.SourceEndPage}";

    private static string DescribeCandidates(
        IReadOnlyList<string> candidates) =>
        candidates.Count == 0
            ? "<none>"
            : string.Join("; ", candidates);

    private static DateOnly? TryParseDate(
        VeteransBlueButtonCareSummaryRecord record) =>
        DateOnly.TryParse(
            record.DateEntered,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var parsed)
                ? parsed
                : null;

    private static string DescribeDateSpan(
        IReadOnlyList<VeteransBlueButtonCareSummaryRecord> records)
    {
        var dates =
            records
                .Select(TryParseDate)
                .Where(date => date.HasValue)
                .Select(date => date!.Value)
                .OrderBy(date => date)
                .ToArray();

        return dates.Length == 0
            ? "<none>"
            : $"{dates[0]:yyyy-MM-dd}..{dates[^1]:yyyy-MM-dd}";
    }

    private static string DescribeBoundaryCandidates(
        IReadOnlyList<VeteransBlueButtonCareSummaryRecord> records)
    {
        if (records.Count == 0)
            return "<none>";

        var selected =
            records.Count <= 6
                ? records
                : records
                    .Take(3)
                    .Concat(records.TakeLast(3))
                    .ToArray();

        return string.Join(
            "; ",
            selected.Select(Describe));
    }

    private static bool MatchesDate(
        VeteransBlueButtonCareSummaryRecord record,
        DateOnly noteDate) =>
        DateOnly.TryParse(
            record.DateEntered,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var parsed) &&
        parsed == noteDate;

    private static bool MatchesTitle(
        VeteransBlueButtonCareSummaryRecord record,
        string noteTitle) =>
        string.Equals(
            record.Title,
            noteTitle,
            StringComparison.OrdinalIgnoreCase) ||
        record.NoteTitles.Any(
            title =>
                string.Equals(
                    title,
                    noteTitle,
                    StringComparison.OrdinalIgnoreCase));
}
