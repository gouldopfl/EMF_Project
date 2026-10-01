using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using EMF.Extensions.VeteransClaims.Orchestration;
using static EMF.Tests.VeteransReviewerEvidencePresentationTests;

namespace EMF.Tests;

public sealed class VeteransReviewerStructuredRecordRecoveryTests
{
    private const string Header = "Collection DT     Specimen   Test Name          Result    Units       Ref";
    private static string Row(string test, string result, string units, string reference) =>
        "09/28/2026 12:14".PadRight(17) + "SERUM".PadRight(11) + test.PadRight(19) + result.PadRight(10) + units.PadRight(12) + reference;
    private const string Provider = "EXAMPLE,\nJANE A, PT DPT\n- 09/25/26 11:30\nEDT";
    private const string Metadata = "Name of Problem:   Synthetic problem ; Onset Date:   09/24/2019 ; Recorder:\n" +
        " SYSTEM, SYSTEM Cerner, Managed\nAcct; Confirmation:   Confirmed ; Classification:   Medical ; Code:\n" +
        " 2797015 ; Contributor System:   SOURCE_SYSTEM ; Last\nUpdated:   06/23/2026 23:19\nEDT ; Life Cycle Status:\n" +
        " Active ; Responsible Provider:   SYSTEM, SYSTEM Cerner,\nManaged Acct; Vocabulary:   SNOMED CT";

    [Fact]
    public void FixedWidthHeaderRecoversEmptyUnitsAndPositionedRangeWithoutChangingTokens()
    {
        var source = new[] { Header, "Range", Row("Sodium", "138", "mmol/L", "135 -"), new string(' ', 69) + "145",
            Row("Clarity", "Clear", "", "Ref:"), new string(' ', 69) + "Clear" };
        var block = Assert.Single(VeteransReviewerClinicalLayout.FindLabBlocks(source)).Value;
        Assert.Equal(6, block.LineCount);
        Assert.Equal(new[] { "09/28/2026 12:14", "SERUM", "Sodium", "138", "mmol/L", "135 - 145" }, block.Rows[1]);
        Assert.Equal(new[] { "09/28/2026 12:14", "SERUM", "Clarity", "Clear", "", "Ref: Clear" }, block.Rows[2]);
        Assert.Empty(block.UnassignedContinuations);
        using var doc = Open(string.Join("\n", source));
        var table = Assert.Single(doc.MainDocumentPart!.Document!.Body!.Elements<Table>().Where(t => t.InnerText.StartsWith("Collection DT")));
        Assert.All(table.Elements<TableRow>(), r => Assert.NotNull(r.TableRowProperties!.GetFirstChild<CantSplit>()));
        Assert.Empty(new OpenXmlValidator().Validate(doc));
    }

    [Fact]
    public void UnpositionedScalarRemainsUnassignedAndKeepsRawSourceOrder()
    {
        var source = new[] { Header, Row("Sodium", "138", "mmol/L", "135 -"), "99", Row("Clarity", "Clear", "", "Ref:"), "Clear" };
        var block = Assert.Single(VeteransReviewerClinicalLayout.FindLabBlocks(source)).Value;
        Assert.Equal("135 -", block.Rows[1][^1]);
        Assert.Equal(new[] { "99" }, block.UnassignedContinuations[1]);
        using var doc = Open(string.Join("\n", source));
        var rows = Assert.Single(doc.MainDocumentPart!.Document!.Body!.Elements<Table>().Where(t => t.InnerText.StartsWith("Collection DT"))).Elements<TableRow>().ToArray();
        var raw = Assert.Single(rows[2].Elements<TableCell>());
        Assert.Equal("99", raw.InnerText);
        Assert.Equal(6, raw.TableCellProperties!.GridSpan!.Val!.Value);
        Assert.All(rows[1].Descendants<Paragraph>(), p => Assert.NotNull(p.ParagraphProperties!.KeepNext));
        Assert.Empty(new OpenXmlValidator().Validate(doc));
    }

    [Fact]
    public void UncertainFixedWidthRowsUseRawAtomicParagraphsWithoutInventingCells()
    {
        const string uncertain = "\"        \"       \"        SQ-EPI                 <1    /HPF         0 -";
        using var doc = Open(Header + "\n" + Row("Sodium", "138", "mmol/L", "135-") + "\n145\n" + uncertain + "\n50");
        var body = doc.MainDocumentPart!.Document!.Body!;
        var raw = Assert.Single(body.Elements<Paragraph>().Where(p => p.InnerText.StartsWith(uncertain)));
        Assert.Equal(new[] { uncertain, "50" }, raw.Descendants<Text>().Select(t => t.Text));
        Assert.Single(raw.Descendants<Break>());
        Assert.NotNull(raw.ParagraphProperties!.KeepLines);
        Assert.DoesNotContain(body.Elements<Table>(), t => t.InnerText.Contains("SQ-EPI"));
        Assert.Empty(new OpenXmlValidator().Validate(doc));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\f")]
    [InlineData("\f145")]
    public void LabRecoveryStopsAtBlankAndSourcePageBoundary(string boundary)
    {
        var source = new[] { Header, Row("Sodium", "138", "mmol/L", "135 -"), boundary, new string(' ', 69) + "145" };
        var block = Assert.Single(VeteransReviewerClinicalLayout.FindLabBlocks(source)).Value;
        Assert.Equal(2, block.LineCount);
        Assert.Equal("135 -", block.Rows[1][^1]);
        Assert.Empty(block.UnassignedContinuations);
        Assert.Empty(VeteransReviewerClinicalLayout.FindLabBlocks([new string(' ', 69) + "145"]));
    }

    [Fact]
    public void AmbiguousLabRowInvalidatesHeaderRatherThanRecoveringLaterRows()
    {
        var source = new[] { Header, Row("Sodium", "138", "mmol/L", "135-145"),
            "09/30/2026 12:14 UNASSIGNEDCOLUMNWITHNOGAPSVALUE", Row("Potassium", "4.2", "mmol/L", "3.5-5.1") };
        var blocks = VeteransReviewerClinicalLayout.FindLabBlocks(source);
        Assert.Equal(2, Assert.Single(blocks).Value.LineCount);
        Assert.DoesNotContain(3, blocks.Keys);
    }

    [Theory]
    [InlineData("")]
    [InlineData("PowerForm Textual Rendition Notes\n")]
    public void RepeatedCompleteProviderSequencesRecoverEvenInPowerForm(string prefix)
    {
        var text = VeteransReviewerClinicalLayout.PrepareStructuredFields(prefix + Provider + "\n" + Provider);
        Assert.Equal(2, text.Split('\n').Count(l => l == "EXAMPLE, JANE A, PT DPT - 09/25/26 11:30\u00a0EDT"));
        using var doc = Open(prefix + Provider + "\n" + Provider);
        var signatures = doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().Where(p => p.InnerText.StartsWith("EXAMPLE,")).ToArray();
        Assert.Equal(2, signatures.Length);
        Assert.All(signatures, p =>
        {
            Assert.Contains("11:30\u00a0EDT", p.InnerText);
            Assert.NotNull(p.ParagraphProperties!.KeepLines);
        });
    }

    [Theory]
    [InlineData("EXAMPLE,\n\nJANE A, PT DPT\n- 09/25/26 11:30\nEDT")]
    [InlineData("EXAMPLE,\n\fJANE A, PT DPT\n- 09/25/26 11:30\nEDT")]
    [InlineData("EXAMPLE,\tJANE A, PT DPT\n- 09/25/26 11:30\nEDT")]
    [InlineData("EXAMPLE,  JANE A, PT DPT\n- 09/25/26 11:30\nEDT")]
    [InlineData("EXAMPLE,\nJANE A, PT DPT\n- 09/25/26\n11:30 EDT")]
    public void CompatibleProviderTokensAcrossAmbiguousBoundariesAreNotJoined(string source)
    {
        Assert.DoesNotContain("EXAMPLE, JANE", VeteransReviewerClinicalLayout.PrepareStructuredFields(source));
    }

    [Theory]
    [InlineData("")]
    [InlineData("PowerForm Textual Rendition Notes\n")]
    public void CompleteSnomedAndSemicolonMetadataRecoverWithoutChangingClinicalTokens(string prefix)
    {
        var source = prefix + "Synthetic problem (SNOMED\nCT\n:2797015 )\n" + Metadata;
        var recovered = VeteransReviewerClinicalLayout.PrepareStructuredFields(source);
        Assert.Contains("Synthetic problem (SNOMED CT :2797015)", recovered);
        var record = Assert.Single(recovered.Split('\n').Where(l => l.StartsWith("Name of Problem:")));
        Assert.Contains("Last Updated: 06/23/2026 23:19\u00a0EDT ; Life Cycle Status: Active", record);
        Assert.Contains("Responsible Provider: SYSTEM, SYSTEM Cerner, Managed Acct; Vocabulary: SNOMED CT", record);
        using var doc = Open(source);
        Assert.Contains(record, doc.MainDocumentPart!.Document!.Body!.InnerText);
    }

    [Theory]
    [InlineData("Name of Problem: X ;\nUnrelated prose\nVocabulary: SNOMED CT")]
    [InlineData("Name of Problem: X ; Code: 123 ;\n\nLast Updated: 09/30/2026 11:30 EDT ; Life Cycle Status: Active ; Vocabulary: SNOMED CT")]
    [InlineData("Name of Problem: X ; Code: 123 ;\n\fLast Updated: 09/30/2026 11:30 EDT ; Life Cycle Status: Active ; Vocabulary: SNOMED CT")]
    [InlineData("Name of Problem: X ; Code: 123 ; Last Updated: 09/30/2026 11:30 EDT ; Life Cycle Status: Active  UnknownColumn ; Vocabulary: SNOMED CT")]
    [InlineData("Synthetic problem (SNOMED CT\n\n:123)")]
    [InlineData("Synthetic problem (SNOMED CT\n\f:123)")]
    [InlineData("Reason for Study:\n\fweakness")]
    public void IncompleteOrAmbiguousProblemAndFieldSequencesRemainUnchanged(string source)
    {
        Assert.Equal(source, VeteransReviewerClinicalLayout.PrepareStructuredFields(source));
    }

    [Fact]
    public void PowerFormPageMarkerDoesNotAuthorizeColumnsOnLaterUnmarkedPages()
    {
        using var doc = Open("PowerForm Textual Rendition Notes\fTest  Result  Units\nA  1  mg\nB  2  mg");
        var body = doc.MainDocumentPart!.Document!.Body!;
        Assert.DoesNotContain(body.Elements<Table>(), t => t.InnerText.StartsWith("TestResultUnits"));
        Assert.Contains(body.Elements<Paragraph>(), p => p.InnerText == "A  1  mg");
        Assert.Contains(body.Elements<Paragraph>(), p => p.InnerText == "B  2  mg");
    }

    [Fact]
    public void PowerFormUnmarkedPagesPreserveUnsupportedFieldFragments()
    {
        using var doc = Open("PowerForm Textual Rendition Notes\fReason\nfor\nStudy:\nweakness");
        var body = doc.MainDocumentPart!.Document!.Body!;
        Assert.DoesNotContain(body.Elements<Paragraph>(), p => p.InnerText == "Reason for Study: weakness");
        Assert.Contains(body.Elements<Paragraph>(), p => p.InnerText == "Reason");
        Assert.Contains(body.Elements<Paragraph>(), p => p.InnerText == "Study:");
        Assert.Contains(body.Elements<Paragraph>(), p => p.InnerText == "weakness");
    }

    [ReviewerLibreOfficeTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LabRowsAndUnassignedFragmentsStayAtomicAcrossOutputPageBoundaries(bool uncertainColumns)
    {
        var rows = Enumerable.Range(0, 65).SelectMany(i => new[] {
            uncertainColumns ? ($"\"        \"       \"        Test{i:D2}").PadRight(46) + $"{200 + i}".PadRight(10) + "mmol/L".PadRight(12) + "135 -"
                : Row($"Test{i:D2}", $"{200 + i}", "mmol/L", "135 -"), "145" });
        using var doc = Open(Header + "\nRange\n" + string.Join("\n", rows));
        using var stream = new MemoryStream();
        doc.Clone(stream).Dispose();
        var bytes = await new EMF.ConsoleApplication.LibreOfficeVeteransReviewerPackageDocumentConverter().ConvertDocxToPdfAsync(stream.ToArray());
        if (Environment.GetEnvironmentVariable("EMF_LAYOUT_ARTIFACT_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(Path.Combine(directory, uncertainColumns ? "raw-lab-boundaries-layout.pdf" : "lab-boundaries-layout.pdf"), bytes);
        }
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(bytes);
        var labPages = pdf.GetPages().Where(p => p.GetWords().Any(w => w.Text.StartsWith("Test"))).ToArray();
        Assert.True(labPages.Length >= 2);
        for (var i = 0; i < 65; i++)
        {
            var matching = labPages.Where(p => p.GetWords().Any(w => w.Text == $"Test{i:D2}")).ToArray();
            Assert.True(matching.Length == 1, $"Test{i:D2} appeared on pages {string.Join(",", matching.Select(p => p.Number))}");
            var page = matching[0];
            var words = page.GetWords().ToArray();
            var test = Assert.Single(words.Where(w => w.Text == $"Test{i:D2}"));
            var result = Assert.Single(words.Where(w => w.Text == $"{200 + i}"));
            Assert.InRange(Math.Abs(test.BoundingBox.Bottom - result.BoundingBox.Bottom), 0, 2);
            Assert.Contains(words, w => w.Text == "145" && w.BoundingBox.Top <= test.BoundingBox.Bottom &&
                test.BoundingBox.Bottom - w.BoundingBox.Bottom < 25);
        }

    }

    [ReviewerLibreOfficeFact]
    public async Task PowerFormSignaturesAndCompleteProblemRecordsRemainTogetherAcrossOutputPages()
    {
        var blocks = Enumerable.Range(0, 24).Select(i =>
            Provider.Replace("EXAMPLE", "EXAMPLE" + (char)('A' + i)) + "\n" +
            $"Record{i:D2} (SNOMED\nCT\n:{600000 + i} )\n" +
            Metadata.Replace("Synthetic problem", $"Record{i:D2}").Replace("2797015", $"{600000 + i}"));
        using var doc = Open("PowerForm Textual Rendition Notes\n" + string.Join("\n", blocks));
        using var stream = new MemoryStream();
        doc.Clone(stream).Dispose();
        var bytes = await new EMF.ConsoleApplication.LibreOfficeVeteransReviewerPackageDocumentConverter().ConvertDocxToPdfAsync(stream.ToArray());
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(bytes);
        var recordPages = pdf.GetPages().Where(p => p.Text.Contains("SNOMED")).ToArray();
        Assert.True(recordPages.Length >= 2);
        for (var i = 0; i < 24; i++)
        {
            var page = Assert.Single(recordPages.Where(p => p.Text.Contains($"Record{i:D2}")));
            Assert.Contains(page.GetWords(), w => w.Text == "Active");
            var expectedCode = $"{600000 + i}";
            Assert.Contains(expectedCode, page.Text);
            Assert.Contains($"Page {page.Number} of {pdf.NumberOfPages}", page.Text);
            var signature = Assert.Single(pdf.GetPages().Where(p => p.Text.Contains("EXAMPLE" + (char)('A' + i))));
            var words = signature.GetWords().ToArray();
            var surname = Assert.Single(words.Where(w => w.Text.StartsWith("EXAMPLE" + (char)('A' + i))));
            Assert.Contains(words, w => w.Text == "EDT" && Math.Abs(w.BoundingBox.Bottom - surname.BoundingBox.Bottom) < 2);
        }
        if (Environment.GetEnvironmentVariable("EMF_LAYOUT_ARTIFACT_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(Path.Combine(directory, "powerform-records-layout.pdf"), bytes);
        }
    }

    private static WordprocessingDocument Open(string text) => WordprocessingDocument.Open(new MemoryStream(
        ReviewerPackageTestPreparation.Render(Details([new VeteransReviewerArtifactContent
        {
            Artifact = new() { Id = new("structured-source"), Name = "Synthetic clinical source", ArtifactType = "file" },
            SourceName = "VA Blue Button Report", Text = text, Appendix = VeteransReviewerPackageAppendix.MedicalEvidence
        }]))), false);
}
