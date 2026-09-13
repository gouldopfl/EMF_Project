using EMF.Core.Models;
using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.Tests;

public sealed class VeteransBlueButtonCareSummaryParserTests
{
    [Fact]
    public void Parse_PreservesOuterRecordsAndNestedAddendum()
    {
        var parser =
            new VeteransBlueButtonCareSummaryParser();

        var records =
            parser.Parse(
            [
                Page(
                    414,
                    """
                    Care summaries and notes
                    Showing 2 records from newest to oldest
                    PRIMARY CARE SECURE MESSAGING
                    Details
                    Date entered: September 1, 2026
                    LOCAL TITLE: PRIMARY CARE SECURE MESSAGING
                    First portion of the message.
                    LOCAL TITLE: Addendum
                    Added information.
                    """),
                Page(
                    415,
                    """
                    Continuation of the first record.
                    SLEEP MED REMOTE PAP FOLLOW-UP NOTE
                    Details
                    Date entered: August 30, 2026
                    LOCAL TITLE: SLEEP MED REMOTE PAP FOLLOW-UP NOTE
                    PAP therapy remains clinically relevant.
                    Vaccines
                    """)
            ]);

        Assert.Equal(2, records.Count);

        var first = records[0];
        Assert.Equal(
            "PRIMARY CARE SECURE MESSAGING",
            first.Title);
        Assert.Equal(
            "September 1, 2026",
            first.DateEntered);
        Assert.Equal(414, first.SourceStartPage);
        Assert.Equal(415, first.SourceEndPage);
        Assert.Equal(
            [
                "PRIMARY CARE SECURE MESSAGING",
                "Addendum"
            ],
            first.NoteTitles);
        Assert.Contains(
            "Continuation of the first record.",
            first.Text);

        var second = records[1];
        Assert.Equal(
            "SLEEP MED REMOTE PAP FOLLOW-UP NOTE",
            second.Title);
        Assert.Equal(415, second.SourceStartPage);
        Assert.Equal(415, second.SourceEndPage);
        Assert.Single(second.NoteTitles);
    }

    [Fact]
    public void Parse_UsesFinalCareSummarySectionMarkers()
    {
        var parser =
            new VeteransBlueButtonCareSummaryParser();

        var records =
            parser.Parse(
            [
                Page(
                    1,
                    """
                    Care summaries and notes
                    Vaccines
                    """),
                Page(
                    20,
                    """
                    Care summaries and notes
                    SLEEP MED SLEEP CLINIC NOTE
                    Details
                    Date entered: July 22, 2026
                    LOCAL TITLE: SLEEP MED SLEEP CLINIC NOTE
                    Relevant clinical content.
                    Vaccines
                    """)
            ]);

        var record = Assert.Single(records);
        Assert.Equal(
            "SLEEP MED SLEEP CLINIC NOTE",
            record.Title);
        Assert.Equal(20, record.SourceStartPage);
    }

    [Fact]
    public void Parse_RejectsMissingCareSummarySection()
    {
        var parser =
            new VeteransBlueButtonCareSummaryParser();

        var ex =
            Assert.Throws<InvalidDataException>(
                () => parser.Parse(
                [
                    Page(
                        1,
                        "Vaccines")
                ]));

        Assert.Equal(
            "Blue Button care summaries section was not found.",
            ex.Message);
    }

    [Fact]
    public void Parse_RejectsOuterRecordWithoutDetailsHeading()
    {
        var parser =
            new VeteransBlueButtonCareSummaryParser();

        var ex =
            Assert.Throws<InvalidDataException>(
                () => parser.Parse(
                [
                    Page(
                        1,
                        """
                        Care summaries and notes
                        SLEEP MED SLEEP CLINIC NOTE
                        Date entered: July 22, 2026
                        LOCAL TITLE: SLEEP MED SLEEP CLINIC NOTE
                        Vaccines
                        """)
                ]));

        Assert.Contains(
            "Blue Button outer Details heading was not found",
            ex.Message);
    }

    [Fact]
    public void Parse_ReturnsEmptyForNoPages()
    {
        var parser =
            new VeteransBlueButtonCareSummaryParser();

        var records =
            parser.Parse([]);

        Assert.Empty(records);
    }

    private static ExtractedArtifactTextPage Page(
        int pageNumber,
        string text) =>
        new()
        {
            PageNumber = pageNumber,
            Text = text
        };
}
