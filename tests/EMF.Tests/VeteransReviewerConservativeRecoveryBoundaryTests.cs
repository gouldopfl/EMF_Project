using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.Tests;

public sealed class VeteransReviewerConservativeRecoveryBoundaryTests
{
    private const string Header = "Collection DT     Specimen   Test Name          Result    Units       Ref";
    private static string Row(string name = "Analyte") =>
        "09/30/2026 12:14".PadRight(18) + "SERUM".PadRight(11) + name.PadRight(19) +
        "138".PadRight(10) + "mmol/L".PadRight(12) + "135 -";

    [Theory]
    [InlineData("EXAMPLE,\nJANE A, PT  DPT\n- 09/25/26 11:30\nEDT")]
    [InlineData("EXAMPLE,\nJANE A, PT DPT\n- 09/25/26  11:30\nEDT")]
    [InlineData("EXAMPLE,\nJANE A, PT DPT\n- 09/25/26 11:30\nNOTES")]
    [InlineData("EXAMPLE,\nJANE A, PT DPT\n- 09/25/26 11:30\nEDT  OTHER COLUMN")]
    [InlineData("Date\n/Time:\n09/30/2026  OtherColumn")]
    [InlineData("Date\n/  Time:\n09/30/2026")]
    [InlineData("Other  (SNOMED\nCT\n:123)")]
    [InlineData("Synthetic problem (SNOMED\nCT\n:123)  OtherColumn")]
    [InlineData("Other  09/30/2026\n11\n:\n30")]
    [InlineData("Reason  for Study:\nweakness")]
    [InlineData("Date\n/Time:\n\nweakness")]
    [InlineData("Reason for Study:\fweakness")]
    [InlineData("Reason for Study:\n\fweakness")]
    [InlineData("Reason\n\nfor Study:\nweakness")]
    [InlineData("Synthetic problem (SNOMED\tCT\n:123)")]
    [InlineData("Name of Problem: X ; Code: 123 ;\n\fLast Updated: 09/30/2026 11:30 EDT ; Life Cycle Status: Active ; Vocabulary: SNOMED CT")]
    [InlineData("Name of Problem: X ; Code: 123 ;\nLast Updated: 09/30/2026 11:30 EDT ; Life Cycle Status: Active\tUnknownColumn ; Vocabulary: SNOMED CT")]
    [InlineData("Name of Problem: X ; Code: unknown ;\nLast Updated: 09/30/2026 11:30 EDT ; Life Cycle Status: Active ; Vocabulary: SNOMED CT")]
    public void AmbiguousTokensAndPageMarkersArePreservedExactly(string source)
    {
        Assert.Equal(source, VeteransReviewerClinicalLayout.PrepareStructuredFields(source));
    }

    [Fact]
    public void PowerFormWithCrLfRecoversOnlyACompleteSignature()
    {
        const string source = "PowerForm Textual Rendition Notes\r\nEXAMPLE,\r\nJANE A, PT DPT\r\n- 09/25/26 11:30\r\nEDT";
        Assert.Equal("PowerForm Textual Rendition Notes\nEXAMPLE, JANE A, PT DPT - 09/25/26 11:30\u00a0EDT",
            VeteransReviewerClinicalLayout.PrepareStructuredFields(source));
    }

    [Fact]
    public void PowerFormContextPersistsOnAnUnmarkedSecondPage()
    {
        const string source = "PowerForm Textual Rendition Notes\fReason\nfor\nStudy:\nweakness\n09/30/2026\n11\n:\n30";
        Assert.Equal(source, VeteransReviewerClinicalLayout.PrepareStructuredFields(source));
        const string page = "Reason\nfor\nStudy:\nweakness";
        Assert.Equal(page, VeteransReviewerClinicalLayout.PrepareStructuredFields(page, sourceIsPowerForm: true));
    }

    [Theory]
    [InlineData("09/30/2026 11:30\nEDT", "09/30/2026 11:30\u00a0EDT")]
    [InlineData("Entered On: 09/30/2026 11:30\nEDT", "Entered On: 09/30/2026 11:30\u00a0EDT")]
    [InlineData("09/30/2026 11:30\nNOTES", "09/30/2026 11:30\nNOTES")]
    [InlineData("09/30/2026 11:30\n\nEDT", "09/30/2026 11:30\n\nEDT")]
    [InlineData("09/30/2026 11:30\n\fEDT", "09/30/2026 11:30\n\fEDT")]
    [InlineData("09/30/2026 11:30\n  EDT", "09/30/2026 11:30\n  EDT")]
    public void TimestampAttachmentRequiresACompleteSingleField(string source, string expected)
    {
        Assert.Equal(expected, VeteransReviewerClinicalLayout.PrepareStructuredFields(source));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\f")]
    [InlineData("Unassigned column fragments")]
    [InlineData("145  222")]
    public void UncertainLabBoundaryDisablesFollowingRowsUntilAnotherHeader(string boundary)
    {
        var source = new[] { Header, Row(), boundary, Row("Later") };
        Assert.Equal(2, Assert.Single(VeteransReviewerClinicalLayout.FindLabBlocks(source)).Value.LineCount);
        Assert.Single(VeteransReviewerClinicalLayout.FindAtomicLabRows(source));
    }

    [Fact]
    public void RawLabFallbackPreservesUncertainColumnsAndScalarLinesWithoutAssigningCells()
    {
        const string uncertain = "\"        \"       \"        SQ-EPI                 <1    /HPF         0 -";
        var source = new[] { Header, Row(), "145", uncertain, "50", Row("Later"), "150" };
        var tables = VeteransReviewerClinicalLayout.FindLabBlocks(source);
        Assert.Equal(3, Assert.Single(tables).Value.LineCount);
        var raw = VeteransReviewerClinicalLayout.FindAtomicLabRows(source);
        Assert.Equal(new[] { 1, 3, 5 }, raw.Keys);
        Assert.Equal(new[] { uncertain, "50" }, raw[3].Lines);
        Assert.Equal(2, raw[3].LineCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitLabCommentDoesNotInventCellsOrConsumeCommentProse(bool pageBoundary)
    {
        var source = new[] { Header, Row(), "145", " Comment: Source method unchanged",
            "          continued source method", pageBoundary ? "\f" : Row("Later"), "150" };
        Assert.Equal(3, Assert.Single(VeteransReviewerClinicalLayout.FindLabBlocks(source)).Value.LineCount);
        var raw = VeteransReviewerClinicalLayout.FindAtomicLabRows(source);
        Assert.Equal(pageBoundary ? 1 : 2, raw.Count);
        Assert.DoesNotContain(raw.Values.SelectMany(v => v.Lines), l => l.Contains("method"));
    }

    [Theory]
    [InlineData("Test  Result  Units\nA  1  mg\n\fB  2  mg")]
    [InlineData("Test  Result  Units\f\nA  1  mg\nB  2  mg")]
    public void GenericColumnRecoveryCannotTrimAwaySourcePageBoundaries(string source)
    {
        Assert.Empty(VeteransReviewerClinicalLayout.FindColumnBlocks(source.Split('\n')));
    }
}
