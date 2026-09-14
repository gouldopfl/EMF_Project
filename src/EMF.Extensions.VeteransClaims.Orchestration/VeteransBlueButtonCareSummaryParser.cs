using EMF.Core.Models;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransBlueButtonCareSummaryParser
{
    private const string CareSummariesHeading =
        "Care summaries and notes";

    private static readonly HashSet<string> FollowingSectionHeadings =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Lab and test results",
            "Medical imaging results",
            "Vaccines",
            "Allergies and reactions",
            "Health conditions",
            "Vitals",
            "Medications",
            "My HealtheVet account summary"
        };

    private const string DateEnteredPrefix =
        "Date entered:";

    private const string LocalTitlePrefix =
        "LOCAL TITLE:";

    private const int MaximumDetailsLookbackLines = 24;
    private const int MaximumTitleLookbackLines = 32;

    public IReadOnlyList<VeteransBlueButtonCareSummaryRecord> Parse(
        IReadOnlyList<ExtractedArtifactTextPage> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        if (pages.Count == 0)
            return [];

        var lines = FlattenPages(pages);

        var sectionStart =
            FindLastExactLine(
                lines,
                CareSummariesHeading);

        if (sectionStart < 0)
            throw new InvalidDataException(
                "Blue Button care summaries section was not found.");

        var sectionEnd =
            FindFollowingTopLevelSection(
                lines,
                sectionStart);

        if (sectionEnd < 0)
            throw new InvalidDataException(
                "Blue Button care summaries end section was not found.");

        var dateLines =
            Enumerable.Range(
                    sectionStart + 1,
                    sectionEnd - sectionStart - 1)
                .Where(index =>
                    Normalize(lines[index].Text)
                        .StartsWith(
                            DateEnteredPrefix,
                            StringComparison.OrdinalIgnoreCase))
                .ToArray();

        if (dateLines.Length == 0)
            throw new InvalidDataException(
                "Blue Button care summaries contained no outer records.");

        var starts =
            dateLines
                .Select(dateLine =>
                    FindRecordStart(
                        lines,
                        sectionStart,
                        dateLine))
                .ToArray();

        var records =
            new List<VeteransBlueButtonCareSummaryRecord>(
                dateLines.Length);

        for (var index = 0;
             index < dateLines.Length;
             index++)
        {
            var start = starts[index];
            var end =
                index + 1 < starts.Length
                    ? starts[index + 1] - 1
                    : sectionEnd - 1;

            while (end >= start &&
                   string.IsNullOrWhiteSpace(lines[end].Text))
            {
                end--;
            }

            if (end < start)
                throw new InvalidDataException(
                    "Blue Button care summary record was empty.");

            var dateLine =
                Normalize(lines[dateLines[index]].Text);

            var dateEntered =
                dateLine[DateEnteredPrefix.Length..].Trim();

            var title =
                Normalize(lines[start].Text);

            var noteTitles =
                ExtractNoteTitles(
                    lines,
                    start,
                    end);

            records.Add(
                new VeteransBlueButtonCareSummaryRecord
                {
                    Title = title,
                    DateEntered = dateEntered,
                    SourceStartPage = lines[start].PageNumber,
                    SourceEndPage = lines[end].PageNumber,
                    NoteTitles = noteTitles,
                    Text = JoinRecordText(
                        lines,
                        start,
                        end)
                });
        }

        return records;
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

            var pageLines =
                page.Text
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace('\r', '\n')
                    .Split('\n');

            foreach (var line in pageLines)
            {
                lines.Add(
                    new PageLine(
                        page.PageNumber,
                        line));
            }
        }

        return lines;
    }

    private static int FindRecordStart(
        IReadOnlyList<PageLine> lines,
        int sectionStart,
        int dateLine)
    {
        var detailsMinimum =
            Math.Max(
                sectionStart + 1,
                dateLine - MaximumDetailsLookbackLines);

        var detailsLine = -1;

        for (var index = dateLine - 1;
             index >= detailsMinimum;
             index--)
        {
            if (string.Equals(
                    Normalize(lines[index].Text),
                    "Details",
                    StringComparison.OrdinalIgnoreCase))
            {
                detailsLine = index;
                break;
            }
        }

        if (detailsLine < 0)
            throw new InvalidDataException(
                $"Blue Button outer Details heading was not found before line {dateLine + 1}.");

        var titleMinimum =
            Math.Max(
                sectionStart + 1,
                detailsLine - MaximumTitleLookbackLines);

        for (var index = detailsLine - 1;
             index >= titleMinimum;
             index--)
        {
            var text = Normalize(lines[index].Text);

            if (string.IsNullOrEmpty(text) ||
                text.StartsWith(
                    "Report generated by ",
                    StringComparison.OrdinalIgnoreCase) ||
                text.Contains(
                    "Date of birth:",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return index;
        }

        throw new InvalidDataException(
            $"Blue Button outer record title was not found before line {dateLine + 1}.");
    }

    private static IReadOnlyList<string> ExtractNoteTitles(
        IReadOnlyList<PageLine> lines,
        int start,
        int end)
    {
        var titles = new List<string>();

        for (var index = start;
             index <= end;
             index++)
        {
            var text = lines[index].Text;
            var marker =
                text.IndexOf(
                    LocalTitlePrefix,
                    StringComparison.OrdinalIgnoreCase);

            if (marker < 0)
                continue;

            var title =
                text[(marker + LocalTitlePrefix.Length)..]
                    .Trim();

            if (!string.IsNullOrEmpty(title))
                titles.Add(title);
        }

        return titles;
    }

    private static string JoinRecordText(
        IReadOnlyList<PageLine> lines,
        int start,
        int end) =>
        string.Join(
            Environment.NewLine,
            Enumerable.Range(
                    start,
                    end - start + 1)
                .Select(index => lines[index].Text));

    private static int FindLastExactLine(
        IReadOnlyList<PageLine> lines,
        string expected)
    {
        for (var index = lines.Count - 1;
             index >= 0;
             index--)
        {
            if (string.Equals(
                    Normalize(lines[index].Text),
                    expected,
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindFollowingTopLevelSection(
        IReadOnlyList<PageLine> lines,
        int sectionStart)
    {
        for (var index = sectionStart + 1;
             index < lines.Count;
             index++)
        {
            var raw = lines[index].Text;

            if (string.IsNullOrWhiteSpace(raw) ||
                char.IsWhiteSpace(raw[0]))
            {
                continue;
            }

            if (FollowingSectionHeadings.Contains(
                    Normalize(raw)))
            {
                return index;
            }
        }

        return -1;
    }

    private static string Normalize(string value) =>
        value.Trim();

    private sealed record PageLine(
        int PageNumber,
        string Text);
}
