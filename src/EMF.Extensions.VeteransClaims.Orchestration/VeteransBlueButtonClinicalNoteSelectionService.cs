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

            throw new InvalidDataException(
                $"No Blue Button care-summary record matched " +
                $"{noteDate:yyyy-MM-dd} / '{normalizedTitle}'. " +
                $"Same-date candidates: {DescribeCandidates(sameDate)}. " +
                $"Same-title candidates: {DescribeCandidates(sameTitle)}.");
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
