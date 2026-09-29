using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using EMF.Extensions.VeteransClaims.Orchestration;
using static EMF.Tests.VeteransReviewerEvidencePresentationTests;

namespace EMF.Tests;

public sealed class VeteransReviewerClinicalLayoutTests
{
    private const string HpiOpening = "HPI: This is a 70 year-old MALE with a history significant for HTN, HLD,";
    private const string HpiTail = "psoriatic arthritis, CAD with multiple stents, TIA, CLBP who presents with worsening pain.";
    private const string Hpi = HpiOpening + "\nMDD,\n" + HpiTail;
    private static readonly (string Label, string Score)[] ExpectedScores =
    [
        ("Pain Intensity", "3/5"), ("Personal Care", "2/5"), ("Lifting", "4/5"),
        ("Walking", "4/5"), ("Sitting", "3/5"), ("Standing", "4/5"),
        ("Sleeping", "3/5"), ("Social Life", "1/5"), ("Traveling", "2/5"), ("Homemaking", "3/5")
    ];
    private static string Questionnaire =>
        "Modified Oswestry LBP Questionnaire: total score of 29/50, with higher score indicating higher disability. 58% disability\n" +
        string.Join("\n", ExpectedScores.Select(row => row.Label.PadRight(27) + row.Score)) +
        "\nSOCIAL: unchanged source paragraph.";

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void NarrativeRepairPreservesEveryNonWhitespaceCharacterAndIsIdempotent(string newline)
    {
        var source = Hpi.Replace("\n", newline) + newline + "PLAN: unchanged.";
        var prepared = VeteransReviewerClinicalLayout.PrepareNarrative(source);
        Assert.Equal(WithoutWhitespace(source), WithoutWhitespace(prepared.Text));
        Assert.Equal(HpiOpening + " MDD, " + HpiTail, Assert.Single(prepared.RejoinedNarratives));
        Assert.Contains(newline + "PLAN: unchanged.", prepared.Text);
        Assert.Equal(prepared.Text, VeteransReviewerClinicalLayout.PrepareNarrative(prepared.Text).Text);
    }

    [Fact]
    public void WrappedPrefixAndAnotherAcronymUseTheSameRuleWithoutDiagnosisHardCoding()
    {
        const string source = "History of present illness: The patient has a documented history significant\n" +
            "for COPD, CAD,\nCKD,\nDM,\nchronic symptoms with increased pain during walking.";
        var prepared = VeteransReviewerClinicalLayout.PrepareNarrative(source);
        Assert.Single(prepared.RejoinedNarratives);
        Assert.Contains("COPD, CAD, CKD, DM, chronic symptoms", prepared.Text);
        Assert.Equal(WithoutWhitespace(source), WithoutWhitespace(prepared.Text));
    }

    [Theory]
    [InlineData("\nMDD,\n\n" + HpiTail)]
    [InlineData("\nMDD\n" + HpiTail)]
    [InlineData("\nMDD,\nPLAN: separate clinical field.")]
    [InlineData("\nMDD,\n/es/ EXAMPLE CLINICIAN")]
    [InlineData("\nMDD,\n1. New numbered clinical section.")]
    [InlineData("\nMDD,\nSex/DOB/Age: Male 01/01/1950 76 years")]
    [InlineData("\nMDD,\nvalue   column   another column")]
    [InlineData("\nMDD,\nvalue\tcolumn\tother clinical values")]
    [InlineData("\nMDD,\n| result | value |")]
    [InlineData("\nMDD,\n[ ] Independent structured field")]
    [InlineData("\nMDD,\n3/5")]
    [InlineData("\n\nMDD,\n" + HpiTail)]
    public void NarrativeRepairStopsAtActualStructure(string suffix)
    {
        var source = HpiOpening + suffix;
        var prepared = VeteransReviewerClinicalLayout.PrepareNarrative(source);
        Assert.Equal(source, prepared.Text);
        Assert.Empty(prepared.RejoinedNarratives);
    }

    [Theory]
    [InlineData("ASSESSMENT: This is a long structured diagnostic list ending with HTN, HLD,")]
    [InlineData("HPI: This is a completed clinical history with HTN, HLD.")]
    [InlineData("HPI: Brief HTN, HLD,")]
    public void UnprovedNarrativeRepairFallsBackUnchanged(string opening)
    {
        var source = opening + "\nMDD,\n" + HpiTail;
        Assert.Equal(source, VeteransReviewerClinicalLayout.PrepareNarrative(source).Text);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RenderUsesFullWidthNarrativeForRecoveredHpiOnBothSourcePaths(bool blueButton)
    {
        using var document = Open(RenderBytes(Hpi + "\nPLAN: unchanged.", blueButton));
        var body = document.MainDocumentPart!.Document!.Body!;
        var paragraph = Assert.Single(body.Elements<Paragraph>().Where(p => p.InnerText.StartsWith("HPI:")));
        Assert.Equal(HpiOpening + " MDD, " + HpiTail, paragraph.InnerText);
        Assert.DoesNotContain(body.Descendants<Table>(), t => t.InnerText.Contains("HPI:"));
        Assert.DoesNotContain(body.Descendants<Paragraph>(), p => p.InnerText.Trim() == "MDD,");
        Assert.All(paragraph.Descendants<FontSize>(), f => Assert.Equal("24", f.Val!.Value));
        Assert.Empty(new OpenXmlValidator().Validate(document));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RenderPairsAllTenScoresWithoutChangingTheirOrderFontTotalOrPercentage(bool blueButton)
    {
        using var document = Open(RenderBytes(Questionnaire, blueButton));
        var table = ScoreTable(document);
        var actual = table.Elements<TableRow>().Select(row => row.Elements<TableCell>().ToArray())
            .Select(cells => (Label: cells[0].InnerText, Score: cells[1].InnerText)).ToArray();
        Assert.Equal(ExpectedScores, actual);
        Assert.Equal(TableLayoutValues.Fixed, table.GetFirstChild<TableProperties>()!.TableLayout!.Type!.Value);
        Assert.All(table.Elements<TableRow>(), row => Assert.NotNull(row.GetFirstChild<TableRowProperties>()!.GetFirstChild<CantSplit>()));
        Assert.All(table.Descendants<FontSize>(), f => Assert.Equal("24", f.Val!.Value));
        var mainDocument = document.MainDocumentPart!.Document!;
        Assert.Contains("29/50", mainDocument.InnerText);
        Assert.Contains("58% disability", mainDocument.InnerText);
        Assert.Contains("SOCIAL:", mainDocument.InnerText);
        Assert.DoesNotContain("SOCIAL:", table.InnerText);
        Assert.Empty(new OpenXmlValidator().Validate(document));
    }

    [Fact]
    public void RecordedTotalIsNotRecalculatedEvenWhenItDiffersFromTheRows()
    {
        using var document = Open(RenderBytes(Questionnaire.Replace("29/50", "31/50").Replace("58%", "62%")));
        var mainDocument = document.MainDocumentPart!.Document!;
        Assert.Contains("31/50", mainDocument.InnerText);
        Assert.Contains("62%", mainDocument.InnerText);
        _ = ScoreTable(document);
    }

    [Fact]
    public void ScoreDetectionWorksForAnotherNamedScaleAndPreservesWrittenFractionsAndColons()
    {
        string[] lines = ["Activity rating scale:", "Climbing:  02 / 10", "Reaching:  3 / 10", "Carrying:  10 / 10", "Total: 15/30"];
        var block = Assert.Single(VeteransReviewerClinicalLayout.FindScoreBlocks(lines));
        Assert.Equal(1, block.Key);
        Assert.Equal("Climbing:", block.Value[0].Label);
        Assert.Equal("02 / 10", block.Value[0].Score);
        Assert.Collection(block.Value,
            first => Assert.Equal("02 / 10", first.Score),
            second => Assert.Equal("3 / 10", second.Score),
            third => Assert.Equal("10 / 10", third.Score));
    }

    [Theory]
    [InlineData("Lifting 3/5\nWalking 4/5\nSitting 2/5")]
    [InlineData("Questionnaire:\nLifting 3/5")]
    [InlineData("Questionnaire:\nLifting 3/5\nWalking 4/5")]
    [InlineData("Questionnaire:\nLifting 3/5\nWalking 4/10\nSitting 2/5")]
    [InlineData("Questionnaire:\nMedications:\nLifting 3/5\nWalking 4/5\nSitting 2/5")]
    [InlineData("Questionnaire:\nLifting 3/5\n\nWalking 4/5\nSitting 2/5")]
    [InlineData("Questionnaire:\nLifting\n3/5\nWalking\n4/5\nSitting\n2/5")]
    [InlineData("Questionnaire:\nLifting 03/05/2026\nWalking 04/05/2026\nSitting 02/05/2026")]
    [InlineData("Questionnaire:\nLifting 3/5 mg\nWalking 4/5 mg\nSitting 2/5 mg")]
    [InlineData("Questionnaire:\nLabel 3/5 4/5\nOther 3/5 4/5\nThird 3/5 4/5")]
    [InlineData("Questionnaire:\n| Lifting | 3/5 |\n| Walking | 4/5 |\n| Sitting | 2/5 |")]
    public void ScoreDetectionDoesNotInferAmbiguousOrUnrelatedColumns(string text)
    {
        Assert.Empty(VeteransReviewerClinicalLayout.FindScoreBlocks(text.Split('\n')));
    }

    [Fact]
    public void OversizedCandidateDoesNotProduceAPartialInferredTable()
    {
        var lines = new[] { "Activity questionnaire:" }.Concat(Enumerable.Range(0, 65).Select(i => "Item " + i + " 3/5")).ToArray();
        Assert.Empty(VeteransReviewerClinicalLayout.FindScoreBlocks(lines));
    }

    [Fact]
    public void PowerFormAndDemographicFieldsRemainOutsideBothRecoveryRules()
    {
        const string demographics = "Patient Name: EXAMPLE, CASE\nSex/DOB/Age: Male 01/01/1950 76 years";
        var source = "PowerForm Textual Rendition Notes\n" + demographics + "\n" + Hpi + "\n" + Questionnaire;
        Assert.Equal(source, VeteransReviewerClinicalLayout.PrepareNarrative(source).Text);
        Assert.Empty(VeteransReviewerClinicalLayout.FindScoreBlocks(source.Split('\n')));
        using var document = Open(RenderBytes(source));
        var body = document.MainDocumentPart!.Document!.Body!;
        Assert.Contains("Patient Name: EXAMPLE, CASE", body.InnerText);
        Assert.Contains("Sex/DOB/Age: Male 01/01/1950 76 years", body.InnerText);
        Assert.DoesNotContain(body.Descendants<Table>(), t => t.InnerText.Contains("Pain Intensity"));
    }

    [ReviewerLibreOfficeFact]
    public async Task ConvertedPdfKeepsScoresInOneColumnAndMddWithinItsNarrativeLine()
    {
        var bytes = RenderBytes(Hpi + "\n\n" + Questionnaire);
        using var document = Open(bytes);
        _ = ScoreTable(document);
        var pdfBytes = await new EMF.ConsoleApplication.LibreOfficeVeteransReviewerPackageDocumentConverter()
            .ConvertDocxToPdfAsync(bytes);
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(pdfBytes);
        var allWords = pdf.GetPages().SelectMany(p => p.GetWords()).ToArray();
        var scores = allWords.Where(w => Regex.IsMatch(w.Text, @"^[1-4]/5$")).ToArray();
        Assert.True(scores.Length == ExpectedScores.Length, "Every questionnaire score must be present in the PDF.");
        Assert.InRange(scores.Max(w => w.BoundingBox.Left) - scores.Min(w => w.BoundingBox.Left), 0, .2);
        var page = Assert.Single(pdf.GetPages().Where(p => p.GetWords().Any(w => w.Text == "MDD,")));
        var words = page.GetWords().ToArray();
        var mdd = Assert.Single(words.Where(w => w.Text == "MDD,"));
        Assert.True(words.Count(w => Math.Abs(w.BoundingBox.Bottom - mdd.BoundingBox.Bottom) < .2) > 1,
            "MDD must not be isolated on its own rendered line.");
        Assert.Contains(allWords, w => w.Text.TrimEnd(',') == "29/50");
        Assert.Contains("58%", allWords.Select(w => w.Text));
        if (Environment.GetEnvironmentVariable("EMF_REVIEWER_LAYOUT_ARTIFACTS") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(Path.Combine(directory, "clinical-layout-regression.docx"), bytes);
            await File.WriteAllBytesAsync(Path.Combine(directory, "clinical-layout-regression.pdf"), pdfBytes);
        }
    }

    private static Table ScoreTable(WordprocessingDocument document) => Assert.Single(
        document.MainDocumentPart!.Document!.Body!.Descendants<Table>().Where(t =>
            t.Elements<TableRow>().Count() == 10 && t.InnerText.Contains("Pain Intensity")));
    private static WordprocessingDocument Open(byte[] bytes) => WordprocessingDocument.Open(new MemoryStream(bytes), false);
    private static string WithoutWhitespace(string text) => Regex.Replace(text, @"\s", "");

    private static byte[] RenderBytes(string text, bool blueButton = true)
    {
        var content = new VeteransReviewerArtifactContent
        {
            Artifact = new()
            {
                Id = new("synthetic-clinical-layout"), Name = "Synthetic clinical layout evidence", ArtifactType = "file",
                Metadata = new Dictionary<string, object> { ["evidenceTitle"] = "Synthetic clinical layout evidence" }
            },
            Text = text, SourceName = blueButton ? "VA Blue Button Report" : "Clinical evidence",
            Appendix = VeteransReviewerPackageAppendix.MedicalEvidence
        };
        var result = VeteransReviewerPackageDocxRenderer.Render(Details([content]));
        Assert.Equal(text, content.Text);
        return result;
    }
}
