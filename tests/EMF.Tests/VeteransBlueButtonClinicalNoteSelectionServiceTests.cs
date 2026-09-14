using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.Tests;

public sealed class VeteransBlueButtonClinicalNoteSelectionServiceTests
{
    [Fact]
    public void Select_ReturnsExactDateAndTitleRecord()
    {
        var service =
            new VeteransBlueButtonClinicalNoteSelectionService();

        var expected =
            Record(
                "SLEEP MED TELEPHONE NOTE",
                "June 6, 2025",
                1213,
                1214,
                "Decision to switch from CPAP to empiric ASV.");

        var result =
            service.Select(
                [
                    Record(
                        "PRIMARY CARE NOTE",
                        "June 6, 2025",
                        1212,
                        1212,
                        "Unrelated record."),
                    expected,
                    Record(
                        "SLEEP MED TELEPHONE NOTE",
                        "April 17, 2025",
                        1347,
                        1348,
                        "Earlier sleep record.")
                ],
                new DateOnly(2025, 6, 6),
                "SLEEP MED TELEPHONE NOTE");

        Assert.Same(expected, result);
        Assert.Equal(
            "Decision to switch from CPAP to empiric ASV.",
            result.Text);
    }

    [Fact]
    public void Select_MatchesNestedNoteTitle()
    {
        var service =
            new VeteransBlueButtonClinicalNoteSelectionService();

        var expected =
            new VeteransBlueButtonCareSummaryRecord
            {
                Title = "SLEEP MED REMOTE PAP FOLLOW-UP NOTE",
                DateEntered = "July 8, 2025",
                SourceStartPage = 1140,
                SourceEndPage = 1142,
                NoteTitles =
                [
                    "SLEEP MED REMOTE PAP FOLLOW-UP NOTE",
                    "Addendum"
                ],
                Text = "Residual AHI 32.7. Addendum orders empiric ASV."
            };

        var result =
            service.Select(
                [expected],
                new DateOnly(2025, 7, 8),
                "Addendum");

        Assert.Same(expected, result);
    }

    [Fact]
    public void Select_RejectsMissingRecord()
    {
        var service =
            new VeteransBlueButtonClinicalNoteSelectionService();

        var ex =
            Assert.Throws<InvalidDataException>(
                () =>
                    service.Select(
                        [
                            Record(
                                "SLEEP MED TELEPHONE NOTE",
                                "June 6, 2025",
                                1213,
                                1214,
                                "Record text.")
                        ],
                        new DateOnly(2025, 7, 8),
                        "SLEEP MED TELEPHONE NOTE"));

        Assert.Contains(
            "No Blue Button care-summary record matched",
            ex.Message);
        Assert.Contains("Parsed records: 1", ex.Message);
        Assert.Contains(
            "Parsable date span: 2025-06-06..2025-06-06",
            ex.Message);
        Assert.Contains("Nearest-date candidates:", ex.Message);
        Assert.Contains("Text-title candidates:", ex.Message);
        Assert.Contains("Boundary candidates:", ex.Message);
    }

    [Fact]
    public void Select_RejectsAmbiguousRecord()
    {
        var service =
            new VeteransBlueButtonClinicalNoteSelectionService();

        var ex =
            Assert.Throws<InvalidDataException>(
                () =>
                    service.Select(
                        [
                            Record(
                                "SLEEP MED TELEPHONE NOTE",
                                "June 6, 2025",
                                1213,
                                1214,
                                "First."),
                            Record(
                                "SLEEP MED TELEPHONE NOTE",
                                "June 6, 2025",
                                1215,
                                1215,
                                "Second.")
                        ],
                        new DateOnly(2025, 6, 6),
                        "SLEEP MED TELEPHONE NOTE"));

        Assert.Contains("selection is ambiguous", ex.Message);
    }

    private static VeteransBlueButtonCareSummaryRecord Record(
        string title,
        string dateEntered,
        int startPage,
        int endPage,
        string text) =>
        new()
        {
            Title = title,
            DateEntered = dateEntered,
            SourceStartPage = startPage,
            SourceEndPage = endPage,
            NoteTitles = [title],
            Text = text
        };
}
