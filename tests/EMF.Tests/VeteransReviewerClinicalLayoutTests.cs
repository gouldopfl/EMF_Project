using System.Text.RegularExpressions;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Models.Medications;
using static EMF.Tests.VeteransReviewerEvidencePresentationTests;
using static EMF.Tests.VeteransReviewerNativeEvidencePageTests;

namespace EMF.Tests;

public sealed class VeteransReviewerClinicalLayoutTests
{
    // Shared layout regression fixtures enforce the architectural contract in
    // docs/REVIEWER_PACKAGE_DETERMINISM_CONTRACT.md, without private Gold data.
    private const string HpiOpening = "HPI: This is a 70 year-old MALE with a history significant for HTN, HLD,";
    private const string HpiTail = "psoriatic arthritis, CAD with multiple stents, TIA, CLBP who presents with worsening pain.";
    private const string Hpi = HpiOpening + "\nMDD,\n" + HpiTail;

    [Fact]
    public void NativePageRecoveredHpiPreservesSourceAndSeparatesPatientReport()
    {
        const string next = "They report additional symptoms during prolonged standing and walking.";
        using var fixture = new NativePage(1224, 1584);
        fixture.Line(HpiOpening, 50, x: 150);
        fixture.Line("MDD,", 65);
        fixture.Line(HpiTail + " " + next, 80, x: 150);
        // Two hanging fragments establish the native prose geometry independently
        // of the HPI label or clinical values.
        fixture.Line("The patient continues supervised exercise during the day", 110, x: 150);
        fixture.Line("and", 125);
        fixture.Line("rests between activities as previously instructed.", 140, x: 150);
        fixture.Line("The clinician reviewed the home program with the patient", 170, x: 150);
        fixture.Line("and", 185);
        fixture.Line("recommended continued follow up after therapy.", 200, x: 150);
        var page = fixture.Page();
        var original = page.Content.ToArray();
        var glyphs = page.TextGeometry!.Glyphs.ToArray();
        Assert.NotNull(VeteransReviewerNativeProse.Reconstruct([page]));
        using var document = Open(RenderBytes("", pages: [page]));
        var body = document.MainDocumentPart!.Document!.Body!;
        var paragraph = Assert.Single(body.Elements<Paragraph>().Where(p => p.InnerText.StartsWith("HPI:")));
        Assert.Equal(HpiOpening + " MDD, " + HpiTail, paragraph.InnerText.Replace('\u00a0', ' '));
        Assert.NotNull(paragraph.Elements<Run>().First().RunProperties!.GetFirstChild<Bold>());
        Assert.Equal(next, paragraph.NextSibling<Paragraph>()!.InnerText);
        Assert.Equal(original, page.Content.ToArray());
        Assert.Equal(glyphs, page.TextGeometry.Glyphs);
        Assert.Empty(new OpenXmlValidator().Validate(document));
    }

    [Fact]
    public void RecoveryDoesNotSplitAtAbbreviationsOrInventPatientReportBoundaries()
    {
        const string tail = "chronic symptoms were reviewed by Dr. Example during the encounter.";
        using var document = Open(RenderBytes(HpiOpening + "\nMDD,\n" + tail));
        var paragraph = Assert.Single(document.MainDocumentPart!.Document!.Body!.Elements<Paragraph>()
            .Where(p => p.InnerText.StartsWith("HPI:")));
        Assert.Equal(HpiOpening + " MDD, " + tail, paragraph.InnerText.Replace('\u00a0', ' '));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RecoveredHpiKeepsBoldLabelAcronymGroupAndFollowingNarrativeSeparate(bool blueButton)
    {
        const string next = "She reports additional symptoms with prolonged standing and walking.";
        using var document = Open(RenderBytes(Hpi + " " + next + "\nPLAN: unchanged.", blueButton));
        var body = document.MainDocumentPart!.Document!.Body!;
        var paragraph = Assert.Single(body.Elements<Paragraph>().Where(p => p.InnerText.StartsWith("HPI:")));
        Assert.Equal((HpiOpening + " MDD, " + HpiTail).Replace("HTN, HLD, MDD,", "HTN,\u00a0HLD,\u00a0MDD,"), paragraph.InnerText);
        Assert.Equal("HPI: ", paragraph.Elements<Run>().First().InnerText);
        Assert.NotNull(paragraph.Elements<Run>().First().RunProperties!.GetFirstChild<Bold>());
        Assert.All(paragraph.Elements<Run>().Skip(1), r => Assert.Null(r.RunProperties!.GetFirstChild<Bold>()));
        Assert.Equal("60", paragraph.ParagraphProperties!.SpacingBetweenLines!.After!.Value);
        Assert.Null(paragraph.ParagraphProperties.Indentation);
        Assert.Equal(next, paragraph.NextSibling<Paragraph>()!.InnerText);
        Assert.Empty(new OpenXmlValidator().Validate(document));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ScoreTableKeepsExplicitWidthsWithoutInventingBlankParagraphs(bool blueButton)
    {
        using var document = Open(RenderBytes(Questionnaire, blueButton));
        var table = ScoreTable(document);
        var properties = table.GetFirstChild<TableProperties>()!;
        Assert.Equal(TableWidthUnitValues.Dxa, properties.TableWidth!.Type!.Value);
        Assert.Equal("9360", properties.TableWidth.Width!.Value);
        Assert.Equal("0", properties.TableIndentation!.Width!.Value.ToString());
        Assert.Equal(new[] { "2700", "6660" }, table.GetFirstChild<TableGrid>()!.Elements<GridColumn>().Select(c => c.Width!.Value));
        Assert.All(table.Elements<TableRow>(), row =>
        {
            var cells = row.Elements<TableCell>().ToArray();
            Assert.Equal("2700", cells[0].TableCellProperties!.TableCellWidth!.Width!.Value);
            Assert.Equal("6660", cells[1].TableCellProperties!.TableCellWidth!.Width!.Value);
            Assert.All(cells, c => Assert.Equal(TableWidthUnitValues.Dxa, c.TableCellProperties!.TableCellWidth!.Type!.Value));
        });
        Assert.Contains("Questionnaire:", table.PreviousSibling()!.InnerText);
        Assert.StartsWith("SOCIAL:", table.NextSibling()!.InnerText);
        Assert.Empty(new OpenXmlValidator().Validate(document));
    }

    [Theory]
    [InlineData(true, 3)]
    [InlineData(false, 3)]
    [InlineData(true, 10)]
    [InlineData(false, 10)]
    [InlineData(true, 64)]
    [InlineData(false, 64)]
    public void AnyNamedScaleUsesGoldScorePaginationWithoutLinkingFollowingClinicalSection(bool blueButton, int count)
    {
        var source = SyntheticScale(count);
        using var document = Open(RenderBytes(source, blueButton));
        var table = Assert.Single(document.MainDocumentPart!.Document!.Body!.Elements<Table>()
            .Where(t => t.InnerText.Contains("Activity 001")));
        var rows = table.Elements<TableRow>().ToArray();
        Assert.Equal(count, rows.Length);
        for (var index = 0; index < rows.Length; index++)
        {
            Assert.NotNull(rows[index].GetFirstChild<TableRowProperties>()!.GetFirstChild<CantSplit>());
            Assert.All(rows[index].Elements<TableCell>(), cell =>
            {
                var properties = cell.GetFirstChild<Paragraph>()!.ParagraphProperties!;
                Assert.Equal(index < count - 1, properties.GetFirstChild<KeepNext>() is not null);
                Assert.Null(properties.SpacingBetweenLines!.Before);
                Assert.Equal("60", properties.SpacingBetweenLines.After!.Value);
            });
        }
        Assert.StartsWith("SOCIAL:", table.NextSibling()!.InnerText);
        using var repeated = Open(RenderBytes(source, blueButton));
        Assert.Equal(table.OuterXml, Assert.Single(repeated.MainDocumentPart!.Document!.Body!.Elements<Table>()
            .Where(t => t.InnerText.Contains("Activity 001"))).OuterXml);
        Assert.Empty(new OpenXmlValidator().Validate(document));
    }

    [Fact]
    public void CommonSectionsUseTheSameLayoutAcrossConditionsMedicinesAndClaimTypes()
    {
        string[] conditions = ["Synthetic joint condition", "Synthetic respiratory condition", "Synthetic digestive condition"];
        string[]? expectedHeadings = null;
        string? expectedScoreTable = null;
        string[]? expectedMedicationLayout = null;
        foreach (var condition in conditions)
        foreach (var secondary in new[] { false, true })
        {
            var evidence = new VeteransReviewerArtifactContent
            {
                Artifact = new()
                {
                    Id = new("synthetic-section-contract"), Name = condition + " evidence", ArtifactType = "file",
                    Metadata = new Dictionary<string, object> { ["evidenceTitle"] = condition + " evidence" }
                },
                Text = SyntheticScale(3), SourceName = "VA Blue Button Report",
                Appendix = VeteransReviewerPackageAppendix.MedicalEvidence
            };
            var initial = Details([evidence], secondary ? new("synthetic-basis") : null);
            var entries = Enumerable.Range(1, 2).Select(index => new MedicationLedgerEntry
            {
                Id = new("synthetic-entry-" + index), MedicationLedgerId = new("synthetic-ledger"),
                EntryOrdinal = index, SourceStartPage = 1, SourceEndPage = 1,
                MedicationName = "Example medication for " + condition, Strength = index * 10 + " mg",
                Status = index == 1 ? "expired" : "active", PrescribedDate = new(2026, index, 1),
                Directions = "Synthetic source directions " + index, Indication = "Synthetic recorded indication"
            }).ToArray();
            var details = new VeteransReviewerPackageDetails
            {
                PackageDetails = initial.PackageDetails, Artifacts = initial.Artifacts,
                ArtifactContents = initial.ArtifactContents, VeteranDisplayName = "Example Veteran",
                PackagePreparedBy = "Example Preparer",
                MedicalOpinionRequested = secondary ? new()
                {
                    OpinionText = "Determine whether the Veteran's " + condition +
                        " is at least as likely as not (50 percent or greater probability) proximately due to or the result " +
                        "of the Veteran's service-connected Synthetic basis condition. If causation is not established, " +
                        "assess aggravation and provide a supporting medical rationale."
                } : null,
                MedicationProgressions = [new() { MedicationName = entries[0].MedicationName, Entries = entries }]
            };
            var bytes = ReviewerPackageTestPreparation.Render(details, sourceReviewDate: new(2026, 9, 29));
            var repeatedBytes = ReviewerPackageTestPreparation.Render(details, sourceReviewDate: new(2026, 9, 29));
            Assert.Equal(DocumentPresentationParts(bytes), DocumentPresentationParts(repeatedBytes));
            using var document = Open(bytes);
            var body = document.MainDocumentPart!.Document!.Body!;
            var headings = body.Elements<Paragraph>()
                .Where(p => p.ParagraphProperties?.ParagraphStyleId?.Val == "Heading1")
                .Select(p => p.InnerText).ToArray();
            Assert.Contains("Relevant Medications for Medical Opinion", headings);
            var score = Assert.Single(body.Elements<Table>().Where(t => t.InnerText.Contains("Activity 001")));
            var medicationParagraphs = body.ChildElements
                .SkipWhile(n => n.InnerText != "Relevant Medications for Medical Opinion").Skip(1)
                .TakeWhile(n => n is not Paragraph p || p.ParagraphProperties?.ParagraphStyleId?.Val != "Heading1")
                .OfType<Paragraph>().Where(p => p.ParagraphProperties?.SectionProperties is null).ToArray();
            var medicationLayout = medicationParagraphs.Select(p =>
                p.ParagraphProperties?.OuterXml + "|" + string.Join("|", p.Elements<Run>().Select(r => r.RunProperties?.OuterXml)))
                .ToArray();
            expectedHeadings ??= headings;
            expectedScoreTable ??= score.OuterXml;
            expectedMedicationLayout ??= medicationLayout;
            Assert.Equal(expectedHeadings, headings);
            Assert.Equal(expectedScoreTable, score.OuterXml);
            Assert.Equal(expectedMedicationLayout, medicationLayout);
            foreach (var entry in entries)
            {
                Assert.Contains(entry.MedicationName, body.InnerText);
                Assert.Contains(entry.Strength!, body.InnerText);
                Assert.Contains(entry.Directions!, body.InnerText);
            }
            Assert.Equal(SyntheticScale(3), evidence.Text);
            Assert.Empty(new OpenXmlValidator().Validate(document));
        }
    }

    // Compare every document part, including styles, settings, headers, footers,
    // images and section controls. OPC relationship IDs and ZIP timestamps are
    // packaging metadata; relationship targets and all presentation data remain checked.
    internal static KeyValuePair<string, string>[] DocumentPresentationParts(byte[] bytes)
    {
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        XNamespace relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace references = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var ids = new Dictionary<(string Part, string Id), string>();
        foreach (var entry in archive.Entries.Where(e => e.FullName.EndsWith(".rels", StringComparison.Ordinal)))
        {
            var separator = entry.FullName.LastIndexOf("_rels/", StringComparison.Ordinal);
            var part = entry.FullName[..separator] + entry.FullName[(separator + 6)..^5];
            using var stream = entry.Open();
            foreach (var relationship in XDocument.Load(stream).Root!.Elements())
                ids[(part, relationship.Attribute("Id")!.Value)] =
                    relationship.Attribute("Type")!.Value + "|" + relationship.Attribute("Target")!.Value + "|" +
                    relationship.Attribute("TargetMode")?.Value;
        }
        return archive.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal).Select(entry =>
        {
            using var stream = entry.Open();
            if (!entry.FullName.EndsWith(".xml", StringComparison.Ordinal) &&
                !entry.FullName.EndsWith(".rels", StringComparison.Ordinal))
                return new KeyValuePair<string, string>(entry.FullName, Convert.ToHexString(SHA256.HashData(stream)));
            var xml = XDocument.Load(stream);
            foreach (var element in xml.Descendants())
            foreach (var attribute in element.Attributes().ToArray())
            {
                if (attribute.Name.Namespace == references)
                    attribute.Value = ids[(entry.FullName, attribute.Value)];
                else if (element.Name == relationships + "Relationship" && attribute.Name == "Id")
                    attribute.Value = element.Attribute("Type")!.Value + "|" + element.Attribute("Target")!.Value + "|" +
                        element.Attribute("TargetMode")?.Value;
            }
            return new KeyValuePair<string, string>(entry.FullName, xml.ToString(SaveOptions.DisableFormatting));
        }).ToArray();
    }

    [ReviewerLibreOfficeFact]
    public async Task LongNamedScalePaginatesWithoutDroppingScoresOrShrinkingBodyText()
    {
        var bytes = RenderBytes(SyntheticScale(64));
        var pdfBytes = await new EMF.ConsoleApplication.LibreOfficeVeteransReviewerPackageDocumentConverter()
            .ConvertDocxToPdfAsync(bytes);
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(pdfBytes);
        var pages = pdf.GetPages().Where(p => p.GetWords().Any(w => w.Text is "001" or "064")).ToArray();
        Assert.True(pages.Length >= 2, "A 64-row score table must span pages at normal body size.");
        var words = pdf.GetPages().SelectMany(p => p.GetWords()).ToArray();
        var repeatedPdfBytes = await new EMF.ConsoleApplication.LibreOfficeVeteransReviewerPackageDocumentConverter()
            .ConvertDocxToPdfAsync(RenderBytes(SyntheticScale(64)));
        using var repeatedPdf = UglyToad.PdfPig.PdfDocument.Open(repeatedPdfBytes);
        Assert.Equal(pdf.NumberOfPages, repeatedPdf.NumberOfPages);
        for (var pageNumber = 1; pageNumber <= pdf.NumberOfPages; pageNumber++)
        {
            var page = pdf.GetPage(pageNumber);
            var repeatedPage = repeatedPdf.GetPage(pageNumber);
            Assert.Equal(page.Width, repeatedPage.Width);
            Assert.Equal(page.Height, repeatedPage.Height);
            Assert.Equal(page.GetWords().Select(w => (w.Text, w.BoundingBox)).ToArray(),
                repeatedPage.GetWords().Select(w => (w.Text, w.BoundingBox)).ToArray());
        }
        Assert.Equal(64, words.Count(w => w.Text == "3/10"));
        for (var index = 1; index <= 64; index++)
            Assert.Single(words.Where(w => w.Text == index.ToString("D3")));
        using var document = Open(bytes);
        var table = Assert.Single(document.MainDocumentPart!.Document!.Body!.Elements<Table>()
            .Where(t => t.InnerText.Contains("Activity 001")));
        Assert.All(table.Descendants<FontSize>(), size => Assert.Equal("24", size.Val!.Value));
    }

    private static string SyntheticScale(int count) => "Functional activity rating scale: documented scores\n" +
        string.Join("\n", Enumerable.Range(1, count).Select(index => $"Activity {index:D3} 3/10")) +
        "\nSOCIAL: unchanged source paragraph.";
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
        Assert.Equal(HpiOpening + " MDD, " + HpiTail, paragraph.InnerText.Replace('\u00a0', ' '));
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

    private static byte[] RenderBytes(string text, bool blueButton = true,
        IReadOnlyList<EMF.Core.Models.PrintableArtifactPage>? pages = null)
    {
        var content = new VeteransReviewerArtifactContent
        {
            Artifact = new()
            {
                Id = new("synthetic-clinical-layout"), Name = "Synthetic clinical layout evidence", ArtifactType = "file",
                Metadata = new Dictionary<string, object> { ["evidenceTitle"] = "Synthetic clinical layout evidence" }
            },
            Text = text, PrintablePages = pages ?? [],
            SourceName = blueButton ? "VA Blue Button Report" : "Clinical evidence",
            Appendix = VeteransReviewerPackageAppendix.MedicalEvidence
        };
        var result = ReviewerPackageTestPreparation.Render(Details([content]), sourceReviewDate: new(2026, 9, 29));
        Assert.Equal(text, content.Text);
        return result;
    }
}
