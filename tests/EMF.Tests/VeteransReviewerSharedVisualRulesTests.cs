using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Orchestration;
using static EMF.Tests.VeteransReviewerEvidencePresentationTests;

namespace EMF.Tests;

public sealed class VeteransReviewerSharedVisualRulesTests
{
    [Fact]
    public void CoverScopeIsCenteredBeneathPreparedUsingWithoutChangingItsValues()
    {
        var details = Details([Content("Synthetic source.")]);
        var cover = new ReviewerPackageCover(details.VeteranDisplayName, "Secondary service connection",
            "Synthetic condition", "Synthetic basis", details.PackagePreparedBy, details.PackageDetails.Package.ReviewerRole);
        using var document = WordprocessingDocument.Open(new MemoryStream(
            ReviewerPackageTestPreparation.Render(details, resolvedCover: cover)), false);
        var paragraphs = document.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().ToArray();
        var prepared = Array.FindIndex(paragraphs, p => p.InnerText.StartsWith("Prepared Using:"));
        Assert.Equal(new[] { "Claim type: Secondary service connection", "Claimed condition: Synthetic condition", "Basis: Synthetic basis" },
            paragraphs.Skip(prepared + 1).Take(3).Select(p => p.InnerText));
        Assert.All(paragraphs.Skip(prepared + 1).Take(3), p =>
            Assert.Equal(JustificationValues.Center, p.ParagraphProperties!.Justification!.Val!.Value));
        Assert.Empty(new OpenXmlValidator().Validate(document));
    }

    [Theory]
    [InlineData("VA Blue Button Report")]
    [InlineData("Synthetic medical source")]
    public void MetadataBoundaryBelongsToSourceMetadataAndKeepsWithTheRecord(string source)
    {
        using var document = Open("SOURCE_RECORD_SENTINEL", source);
        var body = document.MainDocumentPart!.Document!.Body!;
        var metadata = Assert.Single(body.Elements<Paragraph>().Where(p => p.InnerText == "Source: " + source));
        Assert.NotNull(metadata.ParagraphProperties!.ParagraphBorders!.BottomBorder);
        Assert.Equal("180", metadata.ParagraphProperties.SpacingBetweenLines!.After!.Value);
        Assert.NotNull(metadata.ParagraphProperties.KeepNext);
        Assert.Equal("SOURCE_RECORD_SENTINEL", metadata.NextSibling<Paragraph>()!.InnerText);
    }

    [Theory]
    [InlineData("The following points were placed:\nAll\n10\npoints\nin\nboth\nears")]
    [InlineData("The following\npoints were\nplaced:\nAll 10 points in both ears")]
    [InlineData("The following points were placed: All 10 points in both ears")]
    public void LongStructuredPromptUsesFullWidthAndKeepsItsCompleteValue(string input)
    {
        using var document = Open(input + "\nPLAN: Unchanged.");
        var body = document.MainDocumentPart!.Document!.Body!;
        var field = Assert.Single(body.Elements<Paragraph>().Where(p => p.InnerText.StartsWith("The following points were placed:")));
        Assert.Equal("The following points were placed: All 10 points in both ears", field.InnerText);
        Assert.NotNull(field.ParagraphProperties!.KeepLines);
        Assert.DoesNotContain(body.Elements<Table>(), t => t.InnerText.Contains("The following points"));
        Assert.Contains("Unchanged.", body.InnerText);
        Assert.Empty(new OpenXmlValidator().Validate(document));
    }

    [Fact]
    public void ReasonForStudyRemainsPairedAndLabValuesOccupyExplicitColumns()
    {
        const string source = "Reason\nfor\nStudy:\nweakness\nLAB RESULTS:\n" +
            "Test          Result     Units     Reference range\n" +
            "Sodium        138        mmol/L    135-145\n" +
            "Potassium     4.2        mmol/L    3.5-5.1\n" +
            "IMPRESSION: Source wording remains unchanged.";
        using var document = Open(source);
        var tables = document.MainDocumentPart!.Document!.Body!.Elements<Table>().ToArray();
        var reason = Assert.Single(tables.Where(t => t.InnerText.StartsWith("Reason for Study:")));
        var reasonRow = Assert.Single(reason.Elements<TableRow>());
        Assert.Equal(new[] { "Reason for Study:", "weakness" }, reasonRow.Elements<TableCell>().Select(c => c.InnerText));
        Assert.NotNull(reasonRow.TableRowProperties!.GetFirstChild<CantSplit>());
        var lab = Assert.Single(tables.Where(t => t.InnerText.StartsWith("TestResult")));
        Assert.Equal(4, lab.GetFirstChild<TableGrid>()!.Elements<GridColumn>().Count());
        Assert.Equal(new[] { "Sodium", "138", "mmol/L", "135-145" }, lab.Elements<TableRow>().ElementAt(1)
            .Elements<TableCell>().Select(c => c.InnerText));
        Assert.Equal(new[] { "Potassium", "4.2", "mmol/L", "3.5-5.1" }, lab.Elements<TableRow>().ElementAt(2)
            .Elements<TableCell>().Select(c => c.InnerText));
        Assert.All(lab.Descendants<TableCellWidth>(), w => Assert.Equal(TableWidthUnitValues.Dxa, w.Type!.Value));
        Assert.NotNull(lab.Elements<TableRow>().First().TableRowProperties!.GetFirstChild<TableHeader>());
        Assert.Empty(new OpenXmlValidator().Validate(document));
    }

    [Fact]
    public void ImagingColumnsPreserveStudyDateAndStatusAssociations()
    {
        var rows = VeteransReviewerClinicalLayout.FindColumnBlocks([
            "Study     Date          Result", "Chest     09/30/2026    Pending", "Spine     09/29/2026    Final"]);
        var table = Assert.Single(rows).Value;
        Assert.Equal(new[] { "Chest", "09/30/2026", "Pending" }, table[1]);
        Assert.Equal(new[] { "Spine", "09/29/2026", "Final" }, table[2]);
    }

    [Fact]
    public void FragmentedProblemListAndDateTimeRecoverOnlyExplicitTokens()
    {
        const string source = "Problem\nList\nDate\nentered:\n09/30/2026\n14\n:\n35\n" +
            "Problem: Synthetic source condition\nStatus: Active\nPLAN: Original source plan.";
        var normalized = VeteransReviewerClinicalLayout.PrepareStructuredFields(source);
        Assert.Contains("Problem List", normalized);
        Assert.Contains("Date entered: 09/30/2026 14:35", normalized);
        using var document = Open(source);
        var date = Assert.Single(document.MainDocumentPart!.Document!.Body!.Elements<Table>()
            .Where(t => t.InnerText.StartsWith("Date entered:")));
        Assert.Equal("09/30/2026 14:35", date.Elements<TableRow>().Single().Elements<TableCell>().Last().InnerText);
        Assert.Contains("Synthetic source condition", document.MainDocumentPart.Document.Body.InnerText);
        Assert.Contains("Active", document.MainDocumentPart.Document.Body.InnerText);
    }

    [Theory]
    [InlineData("Test  Result  Units\nSodium  138\nUnassigned values  4.2  mmol/L")]
    [InlineData("PowerForm\nTest  Result  Units\nSodium  138  mmol/L\nPotassium  4.2  mmol/L")]
    [InlineData("Sodium\n138\nmmol/L\nPotassium\n4.2\nmmol/L")]
    public void UncertainColumnsAreNotAssignedToInferredCells(string source)
    {
        Assert.Empty(VeteransReviewerClinicalLayout.FindColumnBlocks(source.Split('\n')));
    }

    [Fact]
    public void StructuredRecoveryStopsAtBlankOrNewFieldBoundaries()
    {
        const string source = "Reason for Study:\n\nweakness\nStatus: Active\n" +
            "The following points were placed:\nPLAN: Original plan.\n" +
            "PowerForm unknown columns";
        Assert.Equal(source, VeteransReviewerClinicalLayout.PrepareStructuredFields(source));
    }

    [ReviewerLibreOfficeFact]
    public async Task StructuredFieldsAndLabColumnsRemainAlignedInTheConvertedPdf()
    {
        const string source = "The following points were placed:\nAll\n10\npoints\nin\nboth\nears\n" +
            "Reason for Study:\nweakness\nLAB RESULTS:\n" +
            "Test          Result     Units     Reference range\n" +
            "Sodium        138        mmol/L    135-145\n" +
            "Potassium     4.2        mmol/L    3.5-5.1\n" +
            "PLAN: Original source plan.";
        var docx = ReviewerPackageTestPreparation.Render(Details([Content(source)]));
        var bytes = await new EMF.ConsoleApplication.LibreOfficeVeteransReviewerPackageDocumentConverter()
            .ConvertDocxToPdfAsync(docx);
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(bytes);
        var page = Assert.Single(pdf.GetPages().Where(p => p.Text.Contains("weakness")));
        var words = page.GetWords().ToArray();
        var weakness = Assert.Single(words.Where(w => w.Text == "weakness"));
        var reason = Assert.Single(words.Where(w => w.Text == "Reason"));
        Assert.InRange(Math.Abs(weakness.BoundingBox.Bottom - reason.BoundingBox.Bottom), 0, 2);
        var sodium = Assert.Single(words.Where(w => w.Text == "Sodium"));
        var potassium = Assert.Single(words.Where(w => w.Text == "Potassium"));
        Assert.InRange(Math.Abs(sodium.BoundingBox.Left - potassium.BoundingBox.Left), 0, 1);
        var sodiumValue = Assert.Single(words.Where(w => w.Text == "138"));
        var potassiumValue = Assert.Single(words.Where(w => w.Text == "4.2"));
        Assert.InRange(Math.Abs(sodiumValue.BoundingBox.Left - potassiumValue.BoundingBox.Left), 0, 1);
        Assert.InRange(Math.Abs(sodium.BoundingBox.Bottom - sodiumValue.BoundingBox.Bottom), 0, 2);
        Assert.InRange(Math.Abs(potassium.BoundingBox.Bottom - potassiumValue.BoundingBox.Bottom), 0, 2);
        Assert.Contains("All 10 points in both ears", string.Join(" ", words.Select(w => w.Text)));
        if (Environment.GetEnvironmentVariable("EMF_LAYOUT_ARTIFACT_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(Path.Combine(directory, "structured-fields-layout.docx"), docx);
            await File.WriteAllBytesAsync(Path.Combine(directory, "structured-fields-layout.pdf"), bytes);
        }
    }

    private static VeteransReviewerArtifactContent Content(string text, string source = "VA Blue Button Report") => new()
    {
        Artifact = new() { Id = new("visual-source"), Name = "Synthetic clinical source", ArtifactType = "file",
            Metadata = new Dictionary<string, object> { ["evidenceTitle"] = "Synthetic clinical source" } },
        SourceName = source, Text = text, Appendix = VeteransReviewerPackageAppendix.MedicalEvidence
    };
    private static WordprocessingDocument Open(string source, string sourceName = "VA Blue Button Report") =>
        WordprocessingDocument.Open(new MemoryStream(ReviewerPackageTestPreparation.Render(Details([Content(source, sourceName)]))), false);
}
