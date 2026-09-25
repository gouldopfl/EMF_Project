using System.Text;
using System.Runtime.Versioning;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using EMF.ConsoleApplication;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Orchestration.Services;
using EMF.Persistence.Storage;
using EMF.Tests.TestInfrastructure;
using SkiaSharp;
using static EMF.Tests.VeteransReviewerEvidencePresentationTests;

namespace EMF.Tests;

[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
public sealed class VeteransReviewerPresentationPhase2Tests
{
    private const string PackageHeader =
        "CONFIDENTIAL — VETERAN MEDICAL INFORMATION | Veterans Evidence Package for Medical Review";
    [Theory]
    [InlineData(VeteransReviewerPackageAppendix.MedicalEvidence)]
    [InlineData(VeteransReviewerPackageAppendix.MedicalOpinionEvidence)]
    [InlineData(VeteransReviewerPackageAppendix.ServiceRecords)]
    [InlineData(VeteransReviewerPackageAppendix.LayEvidence)]
    [InlineData(VeteransReviewerPackageAppendix.AdjudicativeRecords)]
    [InlineData(VeteransReviewerPackageAppendix.MedicalLiterature)]
    public void Render_ArtifactSectionsHaveDistinctFirstAndContinuationHeaders(string appendix)
    {
        var contents = new[] { Evidence("Alpha Evidence", "First body.", appendix),
            Evidence("Beta Evidence", "Second body.", appendix) };
        using var document = Open(contents);
        var main = document.MainDocumentPart!;
        var sections = main.Document!.Descendants<SectionProperties>()
            .Where(section => section.GetFirstChild<TitlePage>() is not null &&
                section.Elements<HeaderReference>().Any(reference => reference.Type!.Value == HeaderFooterValues.Default &&
                    contents.Any(content => ((HeaderPart)main.GetPartById(reference.Id!)).Header!.InnerText
                        .EndsWith(content.Artifact.Name + " — Continued", StringComparison.Ordinal))))
            .ToArray();
        Assert.Equal(2, sections.Length);
        for (var i = 0; i < sections.Length; i++)
        {
            var section = sections[i];
            string Header(HeaderFooterValues type) => ((HeaderPart)main.GetPartById(
                section.Elements<HeaderReference>().Single(r => r.Type!.Value == type).Id!)).Header!.InnerText;
            Assert.StartsWith(PackageHeader, Header(HeaderFooterValues.First));
            Assert.DoesNotContain("Continued", Header(HeaderFooterValues.First));
            Assert.StartsWith(PackageHeader + contents[i].Artifact.Name + " — Continued",
                Header(HeaderFooterValues.Default));
            Assert.DoesNotContain(contents[1 - i].Artifact.Name, Header(HeaderFooterValues.Default));
            var firstTitle = main.Document.Body!.Elements<Paragraph>().First(p =>
                p.InnerText == contents[i].Artifact.Name && p.ParagraphProperties?.ParagraphStyleId?.Val == "Heading2");
            var continuedPart = (HeaderPart)main.GetPartById(section.Elements<HeaderReference>()
                .Single(r => r.Type!.Value == HeaderFooterValues.Default).Id!);
            var continuedTitle = continuedPart.Header!.Elements<Paragraph>()
                .Single(p => p.InnerText == contents[i].Artifact.Name + " — Continued");
            var titleColor = Assert.Single(firstTitle.Descendants<Color>()).Val!.Value;
            Assert.Equal("365F91", titleColor);
            Assert.All(continuedTitle.Elements<Run>(), run =>
                Assert.Equal(titleColor, run.RunProperties!.GetFirstChild<Color>()!.Val!.Value));
            Assert.DoesNotContain(continuedPart.Header.Elements<Paragraph>().First().Descendants<Color>(), _ => true);
            Assert.Contains(main.Document.Body!.Elements<Paragraph>(), p =>
                p.InnerText == contents[i].Artifact.Name && p.ParagraphProperties?.ParagraphStyleId?.Val == "Heading2");
            Assert.Equal(SectionMarkValues.NextPage, section.GetFirstChild<SectionType>()!.Val!.Value);
            Assert.Null(section.GetFirstChild<PageNumberType>()?.Start);
            Assert.Equal(2, section.Elements<FooterReference>().Count());
            Assert.All(section.Elements<FooterReference>(), reference =>
            {
                var footer = ((FooterPart)main.GetPartById(reference.Id!)).Footer!;
                Assert.Contains("CONFIDENTIAL", footer.InnerText);
                Assert.Contains(footer.Descendants<SimpleField>(), f => f.Instruction!.Value!.Trim() == "PAGE");
                Assert.Contains(footer.Descendants<SimpleField>(), f => f.Instruction!.Value!.Trim() == "NUMPAGES");
            });
        }
        Assert.DoesNotContain(main.Document.Body!.Descendants<TableHeader>(), _ => true);
        var errors = new OpenXmlValidator().Validate(document)
            .Select(error => error.Description + " at " + error.Path?.XPath).ToArray();
        Assert.True(errors.Length == 0, string.Join("\n", errors));
    }

    [Fact]
    public void Render_ContinuationTitleUsesTheSamePrivacySanitizationAsBody()
    {
        using var document = Open([Evidence("Record SSN: 123-45-6789", "Clinical body.")]);
        Assert.DoesNotContain("123-45-6789", document.MainDocumentPart!.Document!.Body!.InnerText);
        Assert.All(document.MainDocumentPart.HeaderParts,
            part => Assert.DoesNotContain("123-45-6789", part.Header!.InnerText));
    }

    [Fact]
    public void Render_SourceTerminalBlanksDoNotAddSectionParagraphs()
    {
        const string source = "RARE\n<1\nDONE\nNEG\n\nFinal source line.\n\n  \n";
        var content = Evidence("First evidence", source);
        using var document = Open([content, Evidence("Next evidence", "Next body.")]);
        var body = document.MainDocumentPart!.Document!.Body!;
        var last = Assert.Single(body.Elements<Paragraph>().Where(p => p.InnerText == "Final source line."));
        Assert.NotNull(last.ParagraphProperties?.GetFirstChild<SectionProperties>());
        Assert.Equal("Next evidence", last.NextSibling<Paragraph>()!.InnerText);
        Assert.Equal(string.Empty, last.PreviousSibling<Paragraph>()!.InnerText);
        Assert.Equal("NEG", last.PreviousSibling<Paragraph>()!.PreviousSibling<Paragraph>()!.InnerText);
        Assert.Equal(source, content.Text);
        Assert.Empty(new OpenXmlValidator().Validate(document));
    }

    [ReviewerLibreOfficeFact]
    public async Task LibreOffice_TerminalSourceBlanksDoNotCreateEmptyContinuationPages()
    {
        var contents = new[]
        {
            Evidence("First evidence", "RARE\n<1\nDONE\nNEG\n\nFinal source line." + new string('\n', 90)),
            Evidence("Next evidence", "Next body.")
        };
        var output = await new VeteransReviewerPackageDocumentOutputService(
            new LibreOfficeVeteransReviewerPackageDocumentConverter())
            .RenderAsync(Details(contents), VeteransReviewerPackageOutputFormat.Both);
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(output.Pdf!);
        var pages = pdf.GetPages().Select(p => p.Text).ToArray();
        var first = Assert.Single(pages.Where(p => p.Contains("Final source line.")));
        Assert.Contains("First evidence", first);
        Assert.DoesNotContain(pages, p => p.Contains("First evidence — Continued"));
        var index = Array.IndexOf(pages, first);
        Assert.Contains("Next body.", pages[index + 1]);
        Assert.DoesNotContain("First evidence", pages[index + 1]);
        for (var i = 0; i < pages.Length; i++)
            Assert.Contains($"Page {i + 1} of {pages.Length}", pages[i]);
    }

    [Fact]
    public void Render_BlueButtonJoinsOnlyUnambiguousProseWrapsAndPreservesStructuredRows()
    {
        const string text = "The patient describes stable symptoms during the recent\n" +
            "visit and requests routine follow up care.\n\n" +
            "Result          Value    Unit\nTemperature     37.2     C\n" +
            "Pulse	72	/min\nDiagnosis: unchanged\nACTIVE\nCOMPLETED\nSIGNED\n" +
            "Dose 5 mg\nis not joined to its preceding measurement.\n" +
            "Form [ ] Yes [X] No\nSymptoms are stable.  Follow up next month.";
        using var document = Open([Evidence("Structured source", text)]);
        var paragraphs = document.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>().ToArray();
        Assert.Contains(paragraphs, p => p.InnerText ==
            "The patient describes stable symptoms during the recent visit and requests routine follow up care.");
        foreach (var row in new[] { "Temperature     37.2     C", "Pulse   72      /min", "Form [ ] Yes [X] No" })
        {
            var p = Assert.Single(paragraphs.Where(p => p.InnerText == row));
            Assert.Equal("DejaVu Sans Mono", p.Descendants<RunFonts>().Single().Ascii!.Value);
            Assert.Equal("18", p.Descendants<FontSize>().Single().Val!.Value);
        }
        foreach (var line in new[] { "ACTIVE", "COMPLETED", "SIGNED", "Dose 5 mg",
                     "is not joined to its preceding measurement.", "Symptoms are stable.  Follow up next month." })
            Assert.Contains(paragraphs, p => p.InnerText == line);
    }

    [Fact]
    public void Corrections_AreExactAuditableAndIdempotent()
    {
        const string input = "The vetern reports a recent visit whic was documented during the\n" +
            "examinaiton. Symptoms showed persisitence over a prlonged\n" +
            "period, adn follow up was recommended.";
        const string expected = "The veteran reports a recent visit which was documented during the examination. Symptoms showed persistence over a prolonged period, and follow up was recommended.";
        var changes = new List<VeteransReviewerTextCorrection>();
        var block = Assert.Single(VeteransReviewerTextLayout.Project(input));
        var output = VeteransReviewerTypographicalCorrections.Apply(block, changes);
        Assert.Equal(expected, output);
        Assert.Equal(6, changes.Count);
        Assert.All(changes, change =>
        {
            Assert.Equal(1, change.ReviewerBlock);
            Assert.Equal(change.Original, block.Text.Substring(change.Offset, change.Original.Length));
            Assert.NotEmpty(change.RuleId);
        });
        var repeated = new List<VeteransReviewerTextCorrection>();
        Assert.Equal(output, VeteransReviewerTypographicalCorrections.Apply(block with { Text = output }, repeated));
        Assert.Empty(repeated);
        var content = Evidence("Narrative", input);
        using var document = Open([content]);
        Assert.Contains(expected, document.MainDocumentPart!.Document!.Body!.InnerText);
        Assert.Contains("Reviewer transcription corrections (original source unchanged)", document.MainDocumentPart.Document.Body.InnerText);
        Assert.Equal(input, content.Text);
    }

    [Theory]
    [InlineData("VA Blue Button Report")]
    [InlineData("Extracted clinical form.txt")]
    public void Render_FragmentedFormRetainsRowsAndUsesConsistentPreformattedLayout(string sourceName)
    {
        // Shape observed in the supplied Medical Evidence: isolated form tokens,
        // checkboxes, and embedded label/value lines, with no cell coordinates.
        const string form = "b.\nc.\nd.\n\"feet\"\nSelect\n[X]\n2.\ndiagnoses\n" +
            "Other diagnosis: Source diagnosis\n  Side affected: Both\nICD code: xxx\n" +
            "Date of diagnosis (right side): 1973\nYes [ ] No [ ] N/A";
        const string narrative = "The patient describes stable symptoms during the recent visit.";
        var content = Evidence("Synthetic form evidence", form + "\n\n" + narrative, sourceName: sourceName);
        using var document = Open([content]);
        var body = document.MainDocumentPart!.Document!.Body!;
        var rows = body.Elements<Paragraph>().Where(p => form.Split('\n').Contains(p.InnerText)).ToArray();
        Assert.Equal(form.Split('\n'), rows.Select(p => p.InnerText));
        Assert.All(rows, p =>
        {
            Assert.Equal("DejaVu Sans Mono", p.Descendants<RunFonts>().Single().Ascii!.Value);
            Assert.Equal("18", p.Descendants<FontSize>().Single().Val!.Value);
            Assert.Equal("0", p.ParagraphProperties!.GetFirstChild<SpacingBetweenLines>()!.After!.Value);
            Assert.Null(p.ParagraphProperties.GetFirstChild<KeepNext>());
        });
        Assert.DoesNotContain(body.Descendants<Table>(), table => table.InnerText.Contains("Side affected"));
        var blank = rows[^1].NextSibling<Paragraph>()!;
        Assert.Equal(string.Empty, blank.InnerText);
        Assert.Equal("0", blank.ParagraphProperties!.GetFirstChild<SpacingBetweenLines>()!.After!.Value);
        Assert.Equal("18", blank.Descendants<FontSize>().Single().Val!.Value);
        var prose = Assert.Single(body.Elements<Paragraph>().Where(p => p.InnerText == narrative));
        Assert.Equal("DejaVu Serif", prose.Descendants<RunFonts>().Single().Ascii!.Value);
        Assert.Equal(form + "\n\n" + narrative, content.Text);
    }

    [Fact]
    public void Layout_FragmentedLabValuesAreNotReconstructedOrJoined()
    {
        const string text = "Collection DT Specimen Test Name\nResult\nRARE\n<1\nDONE\n\nClear\n\nYellow\nNEG\nUnits\n/HPF\nRef\nNONE\n0\nRef:\n-";
        var blocks = VeteransReviewerTextLayout.Project(text);
        Assert.Equal(text.Split('\n'), blocks.Select(block => block.Text));
        // The title, isolated row between blank boundaries, and empty field
        // label are not absorbed by a fragment demonstrated elsewhere.
        Assert.Equal(VeteransReviewerTextShape.Boundary, blocks[0].Shape);
        Assert.All(blocks.Skip(1).Take(4), block => Assert.Equal(VeteransReviewerTextShape.Preformatted, block.Shape));
        Assert.Equal(VeteransReviewerTextShape.Boundary, blocks[6].Shape);
        Assert.All(blocks.Skip(8).Take(7), block => Assert.Equal(VeteransReviewerTextShape.Preformatted, block.Shape));
        Assert.Equal(VeteransReviewerTextShape.Field, blocks[^2].Shape);
        Assert.Equal(VeteransReviewerTextShape.Boundary, blocks[^1].Shape);
        Assert.All(blocks.Where(block => block.Text.Length == 0),
            block => Assert.Equal(VeteransReviewerTextShape.Blank, block.Shape));
        Assert.Equal(Enumerable.Range(1, blocks.Count), blocks.Select(block => block.SourceLine));
    }

    [Theory]
    [InlineData("\n\n")]
    [InlineData("\nReview Summary\n")]
    [InlineData("\nREVIEW SUMMARY\n")]
    [InlineData("\nreview summary\n")]
    [InlineData("\nSummary:\n")]
    [InlineData("\n---\n")]
    [InlineData("\n___\n")]
    [InlineData("\n# Review summary\n")]
    [InlineData("\n2. Review summary\n")]
    public void Layout_FragmentedDetectionStopsAtSectionBoundaries(string boundary)
    {
        const string prefix = "Review Details\nExaminer: Sample Clinician\n";
        const string fragment = "RARE\n<1\nDONE\nNEG\nReading: 37.2 C";
        const string suffix = "Status: Stable\nAppointment: Scheduled";
        var text = prefix + fragment + boundary + suffix;
        var blocks = VeteransReviewerTextLayout.Project(text);
        Assert.Equal(text.Split('\n'), blocks.Select(block => block.Text));
        Assert.All(blocks.Take(2), block => Assert.Equal(VeteransReviewerTextLayout.Classify(block.Text), block.Shape));
        Assert.All(blocks.Skip(2).Take(5), block => Assert.Equal(VeteransReviewerTextShape.Preformatted, block.Shape));
        Assert.All(blocks.Skip(7), block => Assert.Equal(VeteransReviewerTextLayout.Classify(block.Text), block.Shape));
    }

    [Fact]
    public void Layout_SeparateShortGroupsDoNotAccumulateFragmentTokens()
    {
        const string text = "YES\nNO\n\nUP\nDOWN\nStatus: Stable\nA\nB\nValue: Recorded\nC\nD";
        var blocks = VeteransReviewerTextLayout.Project(text);
        Assert.All(blocks, block => Assert.Equal(VeteransReviewerTextLayout.Classify(block.Text), block.Shape));
    }

    [Theory]
    [InlineData("50")]
    [InlineData("5.85")]
    [InlineData("99/67")]
    [InlineData("06/22/2026 12:07")]
    [InlineData("06/22/2026 12:07 SERUM")]
    [InlineData("\" \" \" MICRO (U)")]
    public void Layout_ExplicitDataRowsDoNotNeedAnAdjacentFragment(string row)
    {
        var blocks = VeteransReviewerTextLayout.Project("Review Summary\n\n" + row + "\n\nStatus: Stable");
        Assert.Equal(VeteransReviewerTextShape.DataRow, blocks[2].Shape);
        Assert.Equal(VeteransReviewerTextShape.Boundary, blocks[0].Shape);
        Assert.Equal(VeteransReviewerTextShape.Field, blocks[4].Shape);
        Assert.Equal(row, blocks[2].Text);
    }

    [Theory]
    [InlineData("2. Review summary")]
    [InlineData("06/22/2026 12:07 reviewed the result")]
    [InlineData("\"No change\" was recorded.")]
    public void Layout_DataRecognitionDoesNotAbsorbNumberedHeadingsOrQuotedProse(string row)
    {
        Assert.DoesNotContain(Assert.Single(VeteransReviewerTextLayout.Project(row)).Shape,
            new[] { VeteransReviewerTextShape.Preformatted, VeteransReviewerTextShape.DataRow });
    }

    [ReviewerLibreOfficeFact]
    public async Task LibreOffice_FragmentedMedicalRowsStayCompactWithoutAbsorbingFollowingSection()
    {
        var rowSets = new[] { new[] { "b.", "c.", "d.", "[X]" }, new[] { "RARE", "<1", "DONE", "NEG" } };
        var contents = rowSets.Select((rows, index) => Evidence($"Fragmented Medical Evidence {index}",
            string.Join("\n", rows) + "\nReading: 37.2 C\n\n50\n\n\" \" \" MICRO (U)\nClear\nRef:\nRef:\nRef:\nReview Summary\nStatus: Stable")).ToArray();
        var output = await new VeteransReviewerPackageDocumentOutputService(
            new LibreOfficeVeteransReviewerPackageDocumentConverter())
            .RenderAsync(Details(contents), VeteransReviewerPackageOutputFormat.Both);
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(output.Pdf!);
        foreach (var rows in rowSets)
        {
            var page = Assert.Single(pdf.GetPages().Where(p => p.Text.Contains(rows[0]) && p.Text.Contains("Reading:")));
            var words = page.GetWords().ToArray();
            var renderedRows = rows.Select(row => Assert.Single(words.Where(word => word.Text == row))).ToArray();
            for (var i = 1; i < renderedRows.Length; i++)
                Assert.InRange((double)(renderedRows[i - 1].Letters[0].StartBaseLine.Y -
                    renderedRows[i].Letters[0].StartBaseLine.Y), 8.5, 13);
            var label = Assert.Single(words.Where(word => word.Text == "Reading:"));
            var value = Assert.Single(words.Where(word => word.Text == "37.2"));
            Assert.InRange((double)label.Letters[0].FontSize, 8.5, 9.5);
            Assert.InRange((double)value.Letters[0].FontSize, 8.5, 9.5);
            Assert.InRange(Math.Abs((double)(label.Letters[0].StartBaseLine.Y - value.Letters[0].StartBaseLine.Y)), 0, 0.1);
            // Inline monospaced label + one space; a synthesized field table
            // instead places the value in a distant second column.
            Assert.InRange((double)(value.BoundingBox.Left - label.BoundingBox.Left), 40, 65);
            foreach (var token in new[] { "50", "MICRO", "Clear" })
                Assert.InRange((double)Assert.Single(words.Where(word => word.Text == token)).Letters[0].FontSize, 8.5, 9.5);
            var repeatedLabels = words.Where(word => word.Text == "Ref:").ToArray();
            Assert.Equal(3, repeatedLabels.Length);
            Assert.All(repeatedLabels, word => Assert.InRange((double)word.Letters[0].FontSize, 8.5, 9.5));
            var status = Assert.Single(words.Where(word => word.Text == "Status:"));
            Assert.InRange((double)status.Letters[0].FontSize, 11.5, 12.5);
            Assert.True(status.Letters[0].StartBaseLine.Y < value.Letters[0].StartBaseLine.Y);
            var heading = Assert.Single(words.Where(word => word.Text == "Summary"));
            Assert.InRange((double)heading.Letters[0].FontSize, 11.5, 12.5);
        }
        using var docx = WordprocessingDocument.Open(new MemoryStream(output.Docx!), false);
        Assert.DoesNotContain(docx.MainDocumentPart!.Document!.Body!.Descendants<Table>(),
            table => table.InnerText.Contains("Reading:"));
    }

    [Fact]
    public void Layout_RepeatedEmptyLabelsDoNotAbsorbFollowingFieldsOrOtherLabels()
    {
        const string source = "Ref:\nRef:\nRef:\nStatus: Stable\nSummary:\nReview:\n\nRef:\nRef:\nValue: recorded:\nValue: recorded:\nValue: recorded:";
        var blocks = VeteransReviewerTextLayout.Project(source);
        Assert.Equal(source.Split('\n'), blocks.Select(block => block.Text));
        Assert.All(blocks.Take(3), block => Assert.Equal(VeteransReviewerTextShape.DataRow, block.Shape));
        Assert.All(blocks.Skip(3), block => Assert.Equal(VeteransReviewerTextLayout.Classify(block.Text), block.Shape));
    }

    [Theory]
    [InlineData("\" \" \" Test", "\n\n")]
    [InlineData("\" \" \" Test", "\nReview Summary\n")]
    [InlineData("\" \" \" Test", "\nSummary:\n")]
    [InlineData("\" \" \" Test", "\nExaminer: Sample Clinician\n")]
    [InlineData("06/22/2026 12:07 SERUM", "\n\n")]
    public void Layout_ExplicitDataAnchorStylesOnlyItsLocalTokens(string anchor, string boundary)
    {
        var source = "Review Details\n" + anchor + "\nClear\nYellow" + boundary + "Pending\nStatus: Stable";
        var blocks = VeteransReviewerTextLayout.Project(source);
        Assert.Equal(source.Split('\n'), blocks.Select(block => block.Text));
        Assert.Equal(VeteransReviewerTextShape.Boundary, blocks[0].Shape);
        Assert.All(blocks.Skip(1).Take(3), block => Assert.Equal(VeteransReviewerTextShape.DataRow, block.Shape));
        Assert.All(blocks.Skip(4), block => Assert.Equal(VeteransReviewerTextLayout.Classify(block.Text), block.Shape));
    }

    private static VeteransReviewerArtifactContent[] DataAnchorBoundaryContents()
    {
        var contents = new List<VeteransReviewerArtifactContent>();
        foreach (var anchor in new[] { "\" \" \" Test", "06/22/2026 12:07 SERUM" })
            foreach (var boundary in new[] { "\n\n", "\nReview Summary\n", "\nExaminer: Sample Clinician\n", "\n2. Review Summary\n" })
                contents.Add(Evidence($"Case {contents.Count:D2}", anchor + "\nClear" + boundary + "Pending\nStatus: Stable"));
        // An explicit source/artifact section break must reset the context too.
        contents.Add(Evidence("Case 08", "06/22/2026 12:07 SERUM\nClear"));
        contents.Add(Evidence("Case 09", "Pending\nStatus: Stable"));
        return contents.ToArray();
    }

    [Fact]
    public void Render_DataAnchorTypographyStopsAtEveryBoundary()
    {
        using var document = Open(DataAnchorBoundaryContents());
        var body = document.MainDocumentPart!.Document!.Body!;
        var paragraphs = body.Descendants<Paragraph>().ToArray();
        foreach (var token in new[] { "\" \" \" Test", "06/22/2026 12:07 SERUM", "Clear" })
        {
            var rows = paragraphs.Where(p => p.InnerText == token).ToArray();
            Assert.NotEmpty(rows);
            Assert.All(rows, p =>
            {
                Assert.Equal("18", p.Descendants<FontSize>().Single().Val!.Value);
                Assert.Equal("DejaVu Sans Mono", p.Descendants<RunFonts>().Single().Ascii!.Value);
                Assert.Null(p.Ancestors<Table>().FirstOrDefault());
            });
        }
        foreach (var token in new[] { "Pending", "Status:", "Examiner:", "Review Summary", "2. Review Summary" })
        {
            var rows = paragraphs.Where(p => p.InnerText == token).ToArray();
            Assert.NotEmpty(rows);
            Assert.All(rows, p => Assert.Equal("24", p.Descendants<FontSize>().Single().Val!.Value));
        }
        Assert.Equal(9, paragraphs.Count(p => p.InnerText == "Clear"));
        Assert.Equal(9, paragraphs.Count(p => p.InnerText == "Pending"));
        Assert.Empty(new OpenXmlValidator().Validate(document));
    }

    [ReviewerLibreOfficeFact]
    public async Task LibreOffice_DataAnchorTypographyStopsAtEveryBoundary()
    {
        var contents = DataAnchorBoundaryContents();
        var output = await new VeteransReviewerPackageDocumentOutputService(
            new LibreOfficeVeteransReviewerPackageDocumentConverter())
            .RenderAsync(Details(contents), VeteransReviewerPackageOutputFormat.Both);
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(output.Pdf!);
        var sourcePages = contents.Select((content, index) => Assert.Single(pdf.GetPages().Where(page =>
            page.Text.Contains(content.Artifact.Name) && page.GetWords().Any(word => word.Text == (index == 9 ? "Pending" : "Clear"))))).ToArray();
        for (var index = 0; index < sourcePages.Length; index++)
        {
            var words = sourcePages[index].GetWords().ToArray();
            var following = Assert.Single(words.Where(word => word.Text == (index == 9 ? "Pending" : "Clear")));
            Assert.InRange((double)following.Letters[0].FontSize, index == 9 ? 11.5 : 8.5, index == 9 ? 12.5 : 9.5);
            if (index < 9)
            {
                var anchor = Assert.Single(words.Where(word => word.Text == (index < 4 ? "Test" : "SERUM")));
                Assert.InRange((double)anchor.Letters[0].FontSize, 8.5, 9.5);
                Assert.InRange((double)(anchor.Letters[0].StartBaseLine.Y - following.Letters[0].StartBaseLine.Y), 8.5, 13);
            }
            if (index != 8)
            {
                foreach (var token in new[] { "Pending", "Status:" })
                    Assert.InRange((double)Assert.Single(words.Where(word => word.Text == token)).Letters[0].FontSize, 11.5, 12.5);
                foreach (var word in words.Where(word => word.Text is "Summary" or "Examiner:"))
                    Assert.InRange((double)word.Letters[0].FontSize, 11.5, 12.5);
            }
        }
        Assert.Equal(sourcePages[8].Number + 1, sourcePages[9].Number);
        Assert.DoesNotContain("Case 08", sourcePages[9].Text);
    }

    [Fact]
    public void Render_BlueButtonWrappedStandaloneMisspellingIsCorrectedWithoutChangingAdjacentText()
    {
        const string narrative = "describes foot adn heel pain as aching, throbbing and increased w/";
        const string producer = "Report generated by My HealtheVet on VA.gov on September 9, 2026";
        const string input = narrative + "\nprlonged\n" + producer;
        var content = Evidence("Wrapped Blue Button note", input);

        Assert.Equal(VeteransReviewerTextShape.Boundary,
            VeteransReviewerTextLayout.Classify("prlonged"));
        var audit = new List<VeteransReviewerTextCorrection>();
        Assert.Equal("  prolonged  ", VeteransReviewerTypographicalCorrections.Apply(
            new("  prlonged  ", VeteransReviewerTextShape.Boundary, 2), audit));
        var correction = Assert.Single(audit);
        Assert.Equal("prlonged", correction.Original);
        Assert.Equal("prolonged", correction.Replacement);
        Assert.Equal(2, correction.ReviewerBlock);

        using var document = Open([content]);
        var body = document.MainDocumentPart!.Document!.Body!;
        var paragraphs = body.Descendants<Paragraph>()
            .Select(paragraph => paragraph.InnerText).ToArray();
        Assert.Contains(narrative.Replace("adn", "and", StringComparison.Ordinal), body.InnerText);
        Assert.Contains("prolonged", paragraphs);
        Assert.DoesNotContain("prlonged", paragraphs);
        Assert.DoesNotContain(producer, body.InnerText);
        Assert.Contains("Reviewer transcription corrections (original source unchanged)",
            body.InnerText);
        Assert.Equal(input, content.Text);
    }

    [Theory]
    [InlineData("COMPLETED")]
    [InlineData("MEDICATIONS")]
    [InlineData("DIAGNOSIS")]
    [InlineData("- prlonged")]
    [InlineData("prlonged 12 mg")]
    [InlineData("Prlonged")]
    [InlineData("ADN")]
    public void Corrections_LeaveNonExactBoundaryBlocksUnchanged(string input)
    {
        var audit = new List<VeteransReviewerTextCorrection>();
        var block = new VeteransReviewerTextBlock(input, VeteransReviewerTextShape.Boundary, 1);
        Assert.Equal(input, VeteransReviewerTypographicalCorrections.Apply(block, audit));
        Assert.Empty(audit);
    }

    [Fact]
    public void Render_BlueButtonProducerGenerationLineIsRemovedOnlyWhenStandalone()
    {
        const string marker = "Report generated by My HealtheVet on VA.gov on September 9, 2026";
        var blue = Evidence("Blue Button source", "Clinical note before.\n" + marker +
            "\nClinical note after.");
        using var document = Open([blue]);
        Assert.Contains("Clinical note before.", document.MainDocumentPart!.Document!.Body!.InnerText);
        Assert.Contains("Clinical note after.", document.MainDocumentPart.Document.Body.InnerText);
        Assert.DoesNotContain(marker, document.MainDocumentPart.Document.Body.InnerText);
        Assert.Equal("Clinical note before.\n" + marker + "\nClinical note after.", blue.Text);

        foreach (var (source, appendix) in new[]
        {
            ("Personal statement", VeteransReviewerPackageAppendix.LayEvidence),
            ("ordinary.pdf", VeteransReviewerPackageAppendix.MedicalEvidence),
            ("ordinary.docx", VeteransReviewerPackageAppendix.MedicalEvidence),
            ("article.pdf", VeteransReviewerPackageAppendix.MedicalLiterature)
        })
        {
            var ordinary = Evidence("Ordinary source", "Clinical note before.\n" + marker +
                "\nClinical note after.", appendix: appendix, sourceName: source);
            using var ordinaryDocument = Open([ordinary]);
            Assert.Contains(marker, ordinaryDocument.MainDocumentPart!.Document!.Body!.InnerText);
        }

        var inline = Evidence("Inline mention", "Clinical note " + marker + " remains narrative.");
        using var inlineDocument = Open([inline]);
        Assert.Contains(marker, inlineDocument.MainDocumentPart!.Document!.Body!.InnerText);
    }

    [Theory]
    [InlineData("The physical EXAMINAITON was reviewed by Dr Vetern.", VeteransReviewerTextShape.Narrative)]
    [InlineData("Medication named prlonged standing remains prescribed.", VeteransReviewerTextShape.Narrative)]
    [InlineData("Diagnosis: persisitence of symptoms", VeteransReviewerTextShape.Field)]
    [InlineData("The identifier is adn-123 on 2026-09-23.", VeteransReviewerTextShape.Narrative)]
    [InlineData("The record states “physical examinaiton” verbatim.", VeteransReviewerTextShape.Narrative)]
    [InlineData("The vetern has a measured value of 12 mg.", VeteransReviewerTextShape.Narrative)]
    [InlineData("prlonged standing   12   mg", VeteransReviewerTextShape.Preformatted)]
    [InlineData("The physical examinaitonx and xexaminaiton were noted.", VeteransReviewerTextShape.Narrative)]
    [InlineData("ADN WHIC OSA GERD remain medical abbreviations.", VeteransReviewerTextShape.Narrative)]
    public void Corrections_LeaveProtectedAndAmbiguousContentUnchanged(string input, object shape)
    {
        var changes = new List<VeteransReviewerTextCorrection>();
        Assert.Equal(input, VeteransReviewerTypographicalCorrections.Apply(new(input, (VeteransReviewerTextShape)shape, 1), changes));
        Assert.Empty(changes);
    }

    [Fact]
    public void Render_MedicationAndDiagnosisSectionsAreNotTypoCorrected()
    {
        const string text = "MEDICATIONS:\nThe physical examinaiton was recorded by the vetern.\n" +
            "ACTIVE\nThe physical examinaiton was recorded by the vetern.\n" +
            "DIAGNOSIS:\nThe physical examinaiton was recorded by the vetern.";
        using var document = Open([Evidence("Protected sections", text)]);
        Assert.DoesNotContain("physical examination", document.MainDocumentPart!.Document!.Body!.InnerText);
        Assert.DoesNotContain("Reviewer transcription corrections", document.MainDocumentPart.Document.Body.InnerText);
    }

    [Theory]
    [InlineData("publication.pdf")]
    [InlineData("publication.docx")]
    public async Task Render_JournalNativePagesPreserveColumnsTablesFiguresAndReviewedRelevance(string name)
    {
        // Entirely synthetic two-column publication with a table continuing onto
        // page two, a figure, and citations. Platform renderer supplies its pages.
        var pdf = JournalPdf();
        var original = pdf.ToArray();
        var renderer = new PdfToImagePageRenderer(dpi: 96);
        var images = new[] { await renderer.RenderPageAsync(pdf, 0), await renderer.RenderPageAsync(pdf, 1) };
        var pages = images.Select((image, index) => new PrintableArtifactPage
            { PageNumber = index + 1, ContentType = "image/png", Content = image }).ToArray();
        var content = Evidence(name, "Flattened text must not replace the journal table.",
            VeteransReviewerPackageAppendix.MedicalLiterature, pages,
            "Reviewer extraction must not replace source figures.", reviewed: true);
        using var document = Open([content]);
        var main = document.MainDocumentPart!;
        Assert.Equal(2, main.ImageParts.Count());
        Assert.Equal(2, main.Document!.Body!.Descendants<Drawing>().Count());
        var embedded = main.ImageParts.Select(part =>
        {
            using var stream = part.GetStream();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        }).ToArray();
        for (var i = 0; i < images.Length; i++)
        {
            var crop = VeteransReviewerSourcePageCrop.Crop(images[i]);
            VeteransReviewerSourcePageCropTests.AssertPreserved(images[i], crop with { Content = embedded[i] });
            Assert.Equal(images[i], pages[i].Content.ToArray());
        }
        Assert.Contains("Relevance: Synthetic reviewed relevance.", main.Document.Body.InnerText);
        Assert.DoesNotContain("Flattened text", main.Document.Body.InnerText);
        Assert.DoesNotContain("Reviewer extraction", main.Document.Body.InnerText);
        Assert.Equal(original, pdf);
    }

    [Fact]
    public void Render_LiteratureFallbackPreservesRowsAndNeverAppliesTypoRules()
    {
        const string text = "Journal Heading\nGroup          Count    Result\nControl        42       0.8\n" +
            "The physical examinaiton was recorded by the vetern.";
        using var document = Open([Evidence("Fallback publication", text, VeteransReviewerPackageAppendix.MedicalLiterature)]);
        var row = Assert.Single(document.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .Where(p => p.InnerText == "Control        42       0.8"));
        Assert.Equal("DejaVu Sans Mono", row.Descendants<RunFonts>().Single().Ascii!.Value);
        Assert.Contains("physical examinaiton", document.MainDocumentPart.Document.Body.InnerText);
    }

    [Fact]
    public void Render_XmlLiteratureFallbackPreservesExplicitCellsAndFigureCaption()
    {
        const string xml = "<article><body><table-wrap><label>Table A</label>" +
            "<caption>Literal study values</caption><table><tr><th>Group</th><th>Result</th></tr>" +
            "<tr><td>Control</td><td>42 mg</td></tr></table></table-wrap>" +
            "<fig><caption>Figure A: reported distribution.</caption></fig></body></article>";
        using var document = Open([Evidence("XML fallback", xml, VeteransReviewerPackageAppendix.MedicalLiterature)]);
        var body = document.MainDocumentPart!.Document!.Body!;
        var row = Assert.Single(body.Descendants<Paragraph>().Where(p => p.InnerText == "Control 42 mg"));
        Assert.Equal("DejaVu Sans Mono", row.Descendants<RunFonts>().Single().Ascii!.Value);
        Assert.Contains("Figure A: reported distribution.", body.InnerText);
        Assert.Contains("Literal study values", body.InnerText);
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("text/plain")]
    [InlineData("application/unknown")]
    public void Render_MalformedPublicationPagesCannotBeBypassedByReviewerText(string type)
    {
        var content = Evidence("Invalid publication", "Fallback must not hide failure.",
            VeteransReviewerPackageAppendix.MedicalLiterature,
            [new PrintableArtifactPage { PageNumber = 1, ContentType = type, Content = new byte[] { 0xff } }],
            "Reviewer text must not hide failure.");
        var exception = Record.Exception(() => VeteransReviewerPackageDocxRenderer.Render(Details([content])));
        if (type == "application/unknown")
            Assert.IsType<NotSupportedException>(exception);
        else
            Assert.IsType<InvalidDataException>(exception);
    }

    [Fact]
    public void Render_OpeningSubsectionsHaveOneLineOfSeparation()
    {
        var empty = Details([]);
        var details = new VeteransReviewerPackageDetails
        {
            PackageDetails = empty.PackageDetails, Artifacts = [],
            MedicalOpinionRequested = new VeteransReviewerMedicalOpinionRequest
            {
                OpinionText = "Provide an independent medical opinion.",
                ApplicableRegulatoryCitations = ["38 CFR 3.310(a)", "38 CFR 3.310(b)"]
            }
        };
        var regulations = details.MedicalOpinionRequested.ApplicableRegulatoryCitations.Select(c =>
            new VeteransReviewerApplicableRegulation
            {
                Citation = c, Text = "Synthetic regulatory test text.", SourceUri = "https://example.invalid",
                SourceSha256 = "test", UpToDateAsOf = new DateOnly(2026, 9, 1),
                RetrievedUtc = DateTimeOffset.UnixEpoch
            }).ToArray();
        using var document = WordprocessingDocument.Open(new MemoryStream(
            VeteransReviewerPackageDocxRenderer.Render(details, regulations)), false);
        var targets = new[] { "Medical Opinion Requested", "How to Use This Package", "Reviewer Guidance",
            "Purpose of This Document", "38 CFR 3.310(a)", "38 CFR 3.310(b)",
            "Applicable VA Regulation: 38 CFR 3.310(a); 38 CFR 3.310(b)" };
        foreach (var target in targets)
        {
            var paragraphs = document.MainDocumentPart!.Document!.Body!.Elements<Paragraph>()
                .Where(p => p.InnerText == target).ToArray();
            Assert.NotEmpty(paragraphs);
            Assert.All(paragraphs, p => Assert.Equal("240",
                p.ParagraphProperties!.GetFirstChild<SpacingBetweenLines>()!.Before!.Value));
        }
    }

    private static VeteransReviewerArtifactContent[] DatedChronologyContents() =>
        Enumerable.Range(1, 16).Select(i =>
        {
            var content = Evidence($"Clinical record {i:D2} with a long descriptive title for the reviewing physician", "Clinical body.");
            ((Dictionary<string, object>)content.Artifact.Metadata)[VeteransArtifactMetadataKeys.EvidenceDate] = $"2025-01-{i:D2}";
            return content;
        }).ToArray();

    [Fact]
    public void Render_ChronologyEntriesDoNotChainWithoutSourceReferences()
    {
        using var document = Open(DatedChronologyContents());
        var entries = document.MainDocumentPart!.Document!.Body!.Elements<Paragraph>()
            .Where(p => p.InnerText.StartsWith("2025-01-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(16, entries.Length);
        Assert.All(entries, p =>
        {
            Assert.False(p.ParagraphProperties?.GetFirstChild<KeepNext>()?.Val?.Value ??
                p.ParagraphProperties?.GetFirstChild<KeepNext>() is not null);
            Assert.NotNull(p.ParagraphProperties?.GetFirstChild<KeepLines>());
        });
    }

    [Fact]
    public void Render_ChronologyEntryStillKeepsItsSourceReference()
    {
        var contents = DatedChronologyContents();
        ((Dictionary<string, object>)contents[0].Artifact.Metadata)[VeteransArtifactMetadataKeys.SourceStartPage] = "12";
        using var document = Open(contents);
        var entry = Assert.Single(document.MainDocumentPart!.Document!.Body!.Elements<Paragraph>()
            .Where(p => p.InnerText.StartsWith("2025-01-01 —", StringComparison.Ordinal)));
        Assert.True(entry.ParagraphProperties!.GetFirstChild<KeepNext>()!.Val!.Value);
        Assert.NotNull(entry.ParagraphProperties.GetFirstChild<KeepLines>());
        Assert.Equal("Appendix A — Medical Evidence | Source page: 12", entry.NextSibling<Paragraph>()!.InnerText);
    }

    [ReviewerLibreOfficeFact]
    public async Task LibreOffice_ChronologyStartsBelowItsIntroduction()
    {
        var output = await new VeteransReviewerPackageDocumentOutputService(
            new LibreOfficeVeteransReviewerPackageDocumentConverter())
            .RenderAsync(Details(DatedChronologyContents()), VeteransReviewerPackageOutputFormat.Both);
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(output.Pdf!);
        var pages = pdf.GetPages().Select(p => p.Text).ToArray();
        var introduction = Assert.Single(pages.Where(p => p.Contains("The chronology below uses evidence dates")));
        Assert.Contains("2025-01-01", introduction);
        for (var i = 1; i <= 16; i++)
            Assert.Single(pages.Where(p => p.Contains($"2025-01-{i:D2} —")));
        for (var i = 0; i < pages.Length; i++)
            Assert.Contains($"Page {i + 1} of {pages.Length}", pages[i]);
    }

    [ReviewerLibreOfficeFact]
    public async Task LibreOffice_PaginatesSyntheticArtifactsWithCorrectContinuationIdentity()
    {
        var contents = new[] { "Alpha", "Beta" }.Select(name => Evidence("Synthetic " + name + " Evidence",
            string.Join("\n", Enumerable.Range(1, 100).Select(i =>
                $"{name} evidence row {i}: This synthetic narrative verifies actual document pagination.")))).ToArray();
        var output = await new VeteransReviewerPackageDocumentOutputService(
            new LibreOfficeVeteransReviewerPackageDocumentConverter())
            .RenderAsync(Details(contents), VeteransReviewerPackageOutputFormat.Both);
        Assert.NotNull(output.Docx);
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(output.Pdf!);
        var pages = pdf.GetPages().Select(page => page.Text).ToArray();
        Assert.Contains("Veteran: Robin Example", pages[0]);
        foreach (var name in new[] { "Alpha", "Beta" })
        {
            var evidencePages = pages.Where(text => text.Contains(name + " evidence row", StringComparison.Ordinal)).ToArray();
            Assert.True(evidencePages.Length > 1);
            Assert.Contains("Synthetic " + name + " Evidence", evidencePages[0]);
            Assert.DoesNotContain("— Continued", evidencePages[0]);
            Assert.All(evidencePages.Skip(1), text => Assert.Contains("Synthetic " + name + " Evidence — Continued", text));
            Assert.All(evidencePages, text =>
            {
                Assert.DoesNotContain("Synthetic " + (name == "Alpha" ? "Beta" : "Alpha") + " Evidence", text);
                Assert.Contains("CONFIDENTIAL", text);
            });
        }
        for (var i = 0; i < pages.Length; i++)
            Assert.Contains($"Page {i + 1} of {pages.Length}", pages[i]);
    }

    [ReviewerLibreOfficeFact]
    public async Task LibreOffice_NativeSourcePagesHaveFirstAndContinuationTitles()
    {
        var pdfSource = JournalPdf();
        var pageRenderer = new PdfToImagePageRenderer(dpi: 96);
        var pages = new[]
        {
            new PrintableArtifactPage { PageNumber = 1, ContentType = "image/png",
                Content = await pageRenderer.RenderPageAsync(pdfSource, 0) },
            new PrintableArtifactPage { PageNumber = 2, ContentType = "image/png",
                Content = await pageRenderer.RenderPageAsync(pdfSource, 1) }
        };
        var contents = new[]
        {
            Evidence("Synthetic PDF Evidence", "unused extracted text", pages: pages),
            Evidence("Synthetic DOCX Evidence", "unused extracted text", pages: pages)
        };
        var output = await new VeteransReviewerPackageDocumentOutputService(
            new LibreOfficeVeteransReviewerPackageDocumentConverter())
            .RenderAsync(Details(contents), VeteransReviewerPackageOutputFormat.Both);
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(output.Pdf!);
        var rendered = pdf.GetPages().Select(page => page.Text).ToArray();
        foreach (var title in contents.Select(c => c.Artifact.Name))
        {
            var evidencePages = pdf.GetPages().Where(page => page.NumberOfImages > 0 &&
                page.Text.Contains(title, StringComparison.Ordinal)).Select(page => page.Text).ToArray();
            Assert.True(evidencePages.Length == 2, string.Join("\n---PAGE---\n", evidencePages));
            Assert.DoesNotContain("— Continued", evidencePages[0]);
            Assert.DoesNotContain("Source Page ", evidencePages[0]);
            Assert.Contains(title + " — Continued", evidencePages[1]);
            Assert.DoesNotContain("Source Page ", evidencePages[1]);
        }
        Assert.All(rendered, text => Assert.DoesNotContain("unused extracted text", text));
    }

    [ReviewerLibreOfficeFact]
    public async Task LibreOffice_LiteratureUsesFullPagesAndHonorsMixedSourceOrientations()
    {
        var source = NativeArticlePdf.Create((612, 792, 0), (792, 612, 0),
            (612, 792, 90), (612, 792, 270), (612, 792, 180));
        var renderer = new PdfToImagePageRenderer(dpi: 144, grayscale: false);
        var pages = new List<PrintableArtifactPage>();
        for (var i = 0; i < 5; i++)
            pages.Add(new() { PageNumber = i + 1, ContentType = "image/png",
                Content = await renderer.RenderPageAsync(source, i) });
        var article = Evidence("Mixed orientation article", "Unusable article extraction",
            VeteransReviewerPackageAppendix.MedicalLiterature, pages, reviewed: true);
        var following = Evidence("ZZ Following article", "Following article fallback text.",
            VeteransReviewerPackageAppendix.MedicalLiterature, reviewed: true);
        var output = await new VeteransReviewerPackageDocumentOutputService(
            new LibreOfficeVeteransReviewerPackageDocumentConverter())
            .RenderAsync(Details([article, following]), VeteransReviewerPackageOutputFormat.Both);
        using var docx = WordprocessingDocument.Open(new MemoryStream(output.Docx!), false);
        Assert.Empty(new OpenXmlValidator().Validate(docx));
        var sections = docx.MainDocumentPart!.Document!.Descendants<SectionProperties>()
            .Where(s => s.GetFirstChild<PageMargin>()!.Left!.Value == VeteransReviewerEvidenceSections.SourceSideMargin)
            .ToArray();
        Assert.Equal(5, sections.Length);
        for (var i = 0; i < pages.Count; i++)
        {
            var landscape = i is 1 or 2 or 3;
            Assert.Equal(landscape ? PageOrientationValues.Landscape : PageOrientationValues.Portrait,
                sections[i].GetFirstChild<PageSize>()!.Orient!.Value);
            var extent = sections[i].Ancestors<Paragraph>().Single()
                .Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent>().Single();
            // Letter pages retain at least 89% of native physical text size.
            Assert.InRange(extent.Cx!.Value / 914400d, landscape ? 9.8 : 7.5, landscape ? 10.3 : 7.8);
            using var embedded = docx.MainDocumentPart.ImageParts.ElementAt(i).GetStream();
            using var copy = new MemoryStream();
            embedded.CopyTo(copy);
            var crop = VeteransReviewerSourcePageCrop.Crop(pages[i].Content);
            VeteransReviewerSourcePageCropTests.AssertPreserved(pages[i].Content,
                crop with { Content = copy.ToArray() });
        }
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(output.Pdf!);
        var sourcePages = pdf.GetPages().Where(p => p.NumberOfImages > 0).ToArray();
        Assert.Equal(5, sourcePages.Length);
        for (var i = 0; i < sourcePages.Length; i++)
        {
            var page = sourcePages[i];
            Assert.Equal(i is 1 or 2 or 3, page.Width > page.Height);
            Assert.Contains($"Source Page {i + 1}", page.Text);
            var image = Assert.Single(page.GetImages());
            Assert.True(image.BoundingBox.Width > (i is 1 or 2 or 3 ? 700 : 540));
            Assert.True(image.BoundingBox.Left >= 24 && image.BoundingBox.Right <= page.Width - 24,
                $"Page {i}: {image.BoundingBox} within {page.Width} x {page.Height}");
            Assert.True(image.BoundingBox.Bottom >= 21 && image.BoundingBox.Top <= page.Height - 35,
                $"Page {i}: {image.BoundingBox} within {page.Width} x {page.Height}");
            if (i > 0) Assert.Equal(sourcePages[i - 1].Number + 1, page.Number);
        }
        var followingPage = Assert.Single(pdf.GetPages().Where(p => p.Text.Contains("Following article fallback text.")));
        Assert.True(followingPage.Height > followingPage.Width);
        Assert.Equal(sourcePages[^1].Number + 1, followingPage.Number);
        Assert.All(pdf.GetPages(), page =>
        {
            Assert.Contains($"Page {page.Number} of {pdf.NumberOfPages}", page.Text);
            Assert.DoesNotContain("Unusable article extraction", page.Text);
        });
    }

    [ReviewerLibreOfficeFact]
    public async Task LibreOffice_DerivedMedicalEvidenceUsesSelectedNativeTableInsteadOfFragmentStreams()
    {
        var directory = Path.Combine(Path.GetTempPath(), "emf-native-source-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSystemArtifactContentStore(directory);
            var source = new Artifact { Id = new("native-source"), Name = "source.pdf", ArtifactType = "file",
                Metadata = new Dictionary<string, object> { [ArtifactMetadataKeys.FileExtension] = ".pdf" } };
            var child = new Artifact { Id = new("derived-excerpt"), Name = "excerpt.txt", ArtifactType = "derived-text",
                Metadata = new Dictionary<string, object>
                {
                    [ArtifactMetadataKeys.FileExtension] = ".txt",
                    [VeteransArtifactMetadataKeys.EvidenceTitle] = "Synthetic Medical Evidence",
                    [VeteransArtifactMetadataKeys.SourceStartPage] = "2",
                    [VeteransArtifactMetadataKeys.SourceEndPage] = "2"
                } };
            var repository = new InMemoryEvidenceRepository();
            await repository.AddArtifactAsync(source);
            await repository.AddArtifactAsync(child);
            await repository.AddRelationshipAsync(new Relationship
            {
                SourceArtifactId = child.Id, TargetArtifactId = source.Id, RelationshipType = RelationshipTypes.DerivedFrom
            });
            var originalPdf = JournalPdf(); // Synthetic two-column table/figure, never clinical content.
            await store.WriteAsync(source.Id, originalPdf);
            var pageRenderer = new PdfToImagePageRenderer(dpi: 96);
            var router = new ArtifactPrintRendererRouter(repository, new DefaultArtifactContentTypeResolver(),
                [new PdfArtifactPrintRenderingProvider(store, pageRenderer), new TextArtifactPrintRenderingProvider(store)]);
            const string extractedText = "UNUSABLE EXTRACTION\nb.\nc.\nResult\nRARE\nUnits\n/HPF";
            await store.WriteAsync(child.Id, Encoding.UTF8.GetBytes(extractedText));
            var details = await VeteransReviewerPackageDetailsServiceTests.AssembleNativeSourcePackageAsync(
                child, repository, router, extractedText);
            var content = Assert.Single(details.ArtifactContents);
            Assert.Equal(VeteransReviewerPackageAppendix.MedicalEvidence, content.Appendix);
            Assert.Equal(source.Id, content.PrintableSourceArtifactId);
            Assert.Equal(child.Id, content.Artifact.Id);
            Assert.Equal(extractedText, content.Text);
            Assert.False(content.IsExtractedTextFallback);
            var output = await new VeteransReviewerPackageDocumentOutputService(new LibreOfficeVeteransReviewerPackageDocumentConverter())
                .RenderAsync(details, VeteransReviewerPackageOutputFormat.Both);
            using var docx = WordprocessingDocument.Open(new MemoryStream(output.Docx!), false);
            var main = docx.MainDocumentPart!;
            var image = Assert.Single(main.ImageParts);
            using var stream = image.GetStream();
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            var nativeRaster = await pageRenderer.RenderPageAsync(originalPdf, 1);
            var crop = VeteransReviewerSourcePageCrop.Crop(nativeRaster);
            VeteransReviewerSourcePageCropTests.AssertPreserved(nativeRaster, crop with { Content = bytes.ToArray() });
            Assert.DoesNotContain("UNUSABLE EXTRACTION", main.Document!.Body!.InnerText);
            Assert.DoesNotContain("was blank", main.Document.Body.InnerText);
            Assert.DoesNotContain("Source Page ", main.Document.Body.InnerText);
            var provenance = Assert.Single(main.Document.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties>());
            Assert.Equal("Source Page 2", provenance.Name!.Value);
            Assert.Contains("Native source page 2", provenance.Description!.Value);
            using var pdf = UglyToad.PdfPig.PdfDocument.Open(output.Pdf!);
            var page = Assert.Single(pdf.GetPages().Where(p => p.NumberOfImages > 0));
            Assert.DoesNotContain("Source Page ", page.Text);
            Assert.Equal(1, page.NumberOfImages);
            Assert.DoesNotContain("Continued", page.Text);
            Assert.All(pdf.GetPages(), p => Assert.DoesNotContain("UNUSABLE EXTRACTION", p.Text));
            Assert.Equal(originalPdf, await store.ReadAsync(source.Id));
            Assert.Equal(Encoding.UTF8.GetBytes(extractedText), await store.ReadAsync(child.Id));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [ReviewerLibreOfficeFact]
    public async Task LibreOffice_GeneratedSubjectsContinueAndCoverUsesOpinionRequest()
    {
        var clinical = Evidence("Synthetic PAP titration study", "Original source record.");
        var literature = Evidence("Synthetic literature", "Original literature.",
            VeteransReviewerPackageAppendix.MedicalLiterature, reviewed: true);
        var basis = Details([clinical, literature]);
        var entries = Enumerable.Range(0, 35).Select(i => new EMF.Extensions.VeteransClaims.Models.Medications.MedicationLedgerEntry
        {
            Id = new("entry-" + i), MedicationLedgerId = new("ledger"), EntryOrdinal = i + 1,
            SourceStartPage = 1, SourceEndPage = 1, MedicationName = $"Medication {i:D2}",
            Status = "active", Strength = "10 mg", PrescribedDate = new DateOnly(2025, 1, 1).AddDays(i),
            Indication = $"INDICATION{i:D2}: documented treatment indication retained from the VA ledger.",
            Directions = $"MEDROW{i:D2}: Take as prescribed with food each morning. Refills: 3. Refills left: 2"
        }).ToArray();
        const string opinion = "Whether the Veteran's synthetic condition is at least as likely as not secondary to the service-connected underlying condition, including aggravation.";
        var details = new VeteransReviewerPackageDetails
        {
            PackageDetails = basis.PackageDetails, Artifacts = basis.Artifacts, ArtifactContents = basis.ArtifactContents,
            VeteranDisplayName = "Robin Example", MedicalOpinionRequested = new() { OpinionText = opinion },
            CurrentMedications = [entries[0]],
            MedicationProgressions = entries.Select(e => new VeteransReviewerMedicationProgression
            {
                MedicationName = e.MedicationName, Entries = [e],
                EntrySources = new Dictionary<EMF.Extensions.VeteransClaims.Models.Identities.MedicationLedgerEntryId, string>
                { [e.Id] = $"SOURCE{e.EntryOrdinal - 1:D2}: VA prescription report, original source page 1." },
                ServiceConnectionBasisId = new("basis"), ServiceConnectionBasisReviewerLabel = "Synthetic basis"
            }).ToArray(),
            ClinicalProgressionEvents = Enumerable.Range(0, 25).Select(i => new VeteransReviewerClinicalProgressionEvent
            {
                ReviewerArtifactId = clinical.Artifact.Id, EventDate = new DateOnly(2025, 1, 1).AddDays(i),
                EventType = EMF.Extensions.VeteransClaims.Models.Clinical.ClinicalProgressionEventTypes.TreatmentUse,
                Summary = $"CLINICALROW{i:D2}: PAP compliance was 98%. Documented treatment use and response remained available for review.",
                SourceLocator = "Synthetic clinical note"
            }).ToArray()
        };
        var output = await new VeteransReviewerPackageDocumentOutputService(
            new LibreOfficeVeteransReviewerPackageDocumentConverter())
            .RenderAsync(details, VeteransReviewerPackageOutputFormat.Both);
        using var docx = WordprocessingDocument.Open(new MemoryStream(output.Docx!), false);
        Assert.Empty(new OpenXmlValidator().Validate(docx));
        var main = docx.MainDocumentPart!;
        var paragraphs = main.Document!.Body!.Elements<Paragraph>().ToArray();
        Assert.Contains(paragraphs.TakeWhile(p => p.ParagraphProperties?.GetFirstChild<SectionProperties>() is null)
            .Select(p => p.InnerText), text => text == "Medical Opinion Requested");
        Assert.Contains(paragraphs, p => p.InnerText.Contains("Refills:\u00a03.\u00a0Refills\u00a0left:\u00a02"));
        Assert.Contains(paragraphs, p => p.InnerText.Contains("Refills\u00a0left:\u00a02"));
        foreach (var entry in entries)
        {
            var heading = paragraphs.First(p => p.InnerText == entry.MedicationName && p.ParagraphProperties?.ParagraphStyleId?.Val == "Heading3");
            Assert.NotNull(heading.ParagraphProperties!.GetFirstChild<KeepNext>());
        }
        foreach (var subject in new[] { "Package Guide", "Clinical Progression", "Relevant Medications for Medical Opinion" })
        {
            var header = main.HeaderParts.SelectMany(h => h.Header!.Elements<Paragraph>())
                .Single(p => p.InnerText == subject + " — Continued");
            var heading = paragraphs.Single(p => p.InnerText == subject && p.ParagraphProperties?.ParagraphStyleId?.Val == "Heading1");
            Assert.Equal(heading.Descendants<Color>().Single().Val!.Value, header.Descendants<Color>().Single().Val!.Value);
        }
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(output.Pdf!);
        var pages = pdf.GetPages().ToArray();
        Assert.Contains("synthetic condition", pages[0].Text);
        Assert.Contains("service-connected underlying condition", pages[0].Text);
        Assert.Contains(pages, p => p.Text.Contains("Package Guide — Continued"));
        foreach (var pair in new[] { ("CLINICALROW", "Clinical Progression"), ("MEDROW", "Relevant Medications for Medical Opinion") })
        {
            var sectionPages = pages.Where(p => p.Text.Contains(pair.Item1) &&
                !p.Text.Contains("Current Medication Use — Reconciled")).ToArray();
            Assert.True(sectionPages.Length > 1);
            Assert.DoesNotContain(pair.Item2 + " — Continued", sectionPages[0].Text);
            Assert.All(sectionPages.Skip(1), p => Assert.Contains(pair.Item2 + " — Continued", p.Text));
        }
        foreach (var entry in entries)
        {
            var page = Assert.Single(pages.Where(p => p.Text.Contains(entry.MedicationName) &&
                !p.Text.Contains("Current Medication Use — Reconciled")));
            Assert.Contains(entry.PrescribedDate!.Value.ToString("MMMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture), page.Text);
            Assert.Contains($"MEDROW{entry.EntryOrdinal - 1:D2}", page.Text);
            Assert.Contains($"INDICATION{entry.EntryOrdinal - 1:D2}", page.Text);
            Assert.Contains($"SOURCE{entry.EntryOrdinal - 1:D2}", page.Text);
        }
        Assert.All(pages, p => Assert.DoesNotContain("Medication record — Continued", p.Text));
        foreach (var page in pages)
            Assert.Contains($"Page {page.Number} of {pages.Length}", page.Text);
    }

    [ReviewerLibreOfficeFact]
    public async Task LibreOffice_OversizedMedicationRecordPreservesOrderWithoutRecordContinuationLabels()
    {
        var basis = Details([Evidence("Synthetic record source", "Original source record.")]);
        var markers = Enumerable.Range(0, 240).Select(i => $"RECORDLINE{i:D3}").ToArray();
        var oversized = new EMF.Extensions.VeteransClaims.Models.Medications.MedicationLedgerEntry
        {
            Id = new("oversized"), MedicationLedgerId = new("ledger"), EntryOrdinal = 1,
            MedicationName = "Example medication", Status = "active",
            PrescribedDate = new DateOnly(2025, 1, 1), SourceStartPage = 1, SourceEndPage = 1,
            Directions = string.Join(" ", markers.Select(m =>
                m + " documents a synthetic dated medication observation with its original ordered wording."))
        };
        var following = new EMF.Extensions.VeteransClaims.Models.Medications.MedicationLedgerEntry
        {
            Id = new("following"), MedicationLedgerId = new("ledger"), EntryOrdinal = 2,
            MedicationName = "Subsequent medication", Status = "active",
            PrescribedDate = new DateOnly(2025, 1, 2), SourceStartPage = 2, SourceEndPage = 2,
            Directions = "FOLLOWINGRECORD: " + string.Join(" ", Enumerable.Repeat(
                "Normal instructions retained together with their source.", 35))
        };
        var details = new VeteransReviewerPackageDetails
        {
            PackageDetails = basis.PackageDetails, Artifacts = basis.Artifacts,
            ArtifactContents = basis.ArtifactContents,
            MedicationProgressions = new[] { oversized, following }.Select(e => new VeteransReviewerMedicationProgression
            {
                MedicationName = e.MedicationName, Entries = [e],
                EntrySources = new Dictionary<MedicationLedgerEntryId, string>
                { [e.Id] = e == oversized ? "OVERSIZEDSOURCEEND: synthetic VA report." : "FOLLOWINGSOURCE: synthetic VA report." }
            }).ToArray()
        };
        var output = await new VeteransReviewerPackageDocumentOutputService(
            new LibreOfficeVeteransReviewerPackageDocumentConverter())
            .RenderAsync(details, VeteransReviewerPackageOutputFormat.Both);
        if (Environment.GetEnvironmentVariable("EMF_REVIEWER_LAYOUT_ARTIFACTS") is { Length: > 0 } artifactDirectory)
        {
            Directory.CreateDirectory(artifactDirectory);
            await File.WriteAllBytesAsync(Path.Combine(artifactDirectory, "Oversized_Record_Regression.docx"), output.Docx!);
            await File.WriteAllBytesAsync(Path.Combine(artifactDirectory, "Oversized_Record_Regression.pdf"), output.Pdf!);
        }
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(output.Pdf!);
        var pages = pdf.GetPages().ToArray();
        var first = Assert.Single(pages.Where(p => p.Text.Contains("Prescribed January 1, 2025"))).Number;
        var last = Assert.Single(pages.Where(p => p.Text.Contains("OVERSIZEDSOURCEEND"))).Number;
        Assert.True(last - first >= 2, "The synthetic record must span at least three pages.");
        // Record-specific continuation labeling remains intentionally unresolved: the current
        // DOCX/LibreOffice header approach cannot reliably distinguish a true within-record
        // continuation from a complete record beginning on a continuation page. Preserve
        // pagination and section headers without reviving either failed labeling prototype.
        const string label = "Medication record — Continued";
        Assert.All(pages, p => Assert.DoesNotContain(label, p.Text));
        var joined = string.Join(" ", pages.Select(p => p.Text));
        var previous = -1;
        foreach (var marker in markers.Append("OVERSIZEDSOURCEEND").Append("FOLLOWINGRECORD").Append("FOLLOWINGSOURCE"))
        {
            var offset = joined.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(offset > previous, $"Missing or reordered marker: {marker}");
            Assert.Equal(offset, joined.LastIndexOf(marker, StringComparison.Ordinal));
            previous = offset;
        }
        var followingPage = Assert.Single(pages.Where(p => p.Text.Contains("FOLLOWINGRECORD")));
        Assert.True(followingPage.Number > last, "The following intact record must begin after the oversized record ends.");
        Assert.DoesNotContain(label, followingPage.Text);
        Assert.Contains("Subsequent medication", followingPage.Text);
        Assert.Contains("FOLLOWINGSOURCE", followingPage.Text);
        Assert.All(pages.Where(p => p.Number > first && p.Number <= last), p =>
        {
            Assert.Contains("Relevant Medications for Medical Opinion — Continued", p.Text);
        });
    }

    private static WordprocessingDocument Open(VeteransReviewerArtifactContent[] contents) =>
        WordprocessingDocument.Open(new MemoryStream(VeteransReviewerPackageDocxRenderer.Render(Details(contents))), false);

    private static VeteransReviewerArtifactContent Evidence(string title, string text,
        string appendix = VeteransReviewerPackageAppendix.MedicalEvidence,
        IReadOnlyList<PrintableArtifactPage>? pages = null, string? reviewerText = null, bool reviewed = false,
        string? sourceName = "VA Blue Button Report")
    {
        var id = new ArtifactId(title);
        return new()
        {
            Artifact = new Artifact { Id = id, Name = title, ArtifactType = "medical-record",
                Metadata = new Dictionary<string, object> { [VeteransArtifactMetadataKeys.EvidenceTitle] = title } },
            Text = text, Appendix = appendix, SourceName = sourceName, PrintablePages = pages ?? [],
            MedicalLiteratureReviewerText = reviewerText,
            ReviewedMedicalLiteratureClassifications = reviewed ? [new()
            {
                ArtifactId = id, Association = new()
                {
                    RequirementId = new("medical-nexus"), MedicalLiteratureSourceId = new("synthetic-publication"),
                    GuidanceRole = "Supporting", Description = "Synthetic reviewed relevance."
                },
                PromotedBy = "test", PromotedUtc = DateTimeOffset.UnixEpoch, ReviewedBy = "test",
                ReviewedUtc = DateTimeOffset.UnixEpoch, IntelligenceOutput = "Previously reviewed material.",
                CapabilityId = "test", ProviderId = "test", CorrelationId = "test", EngineName = "test",
                StartedUtc = DateTimeOffset.UnixEpoch, CompletedUtc = DateTimeOffset.UnixEpoch,
                RequiresReview = false, Warnings = [], SourceExcerpts = []
            }] : []
        };
    }

    private static byte[] JournalPdf()
    {
        using var output = new MemoryStream();
        using var document = SKDocument.CreatePdf(output);
        using var font = new SKFont(SKTypeface.Default, 10);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        for (var page = 1; page <= 2; page++)
        {
            var canvas = document.BeginPage(612, 792);
            canvas.Clear(SKColors.White);
            canvas.DrawText("Synthetic journal: two-column article", 35, 40, SKTextAlign.Left, font, paint);
            for (var column = 0; column < 2; column++)
            {
                var left = 35 + column * 290;
                canvas.DrawText("Table 1" + (page == 2 ? " (continued)" : ""), left, 75, SKTextAlign.Left, font, paint);
                for (var row = 0; row < 12; row++)
                {
                    canvas.DrawText($"Group {page}-{column}-{row}     42     0.8", left, 100 + row * 20,
                        SKTextAlign.Left, font, paint);
                    canvas.DrawLine(left, 105 + row * 20, left + 250, 105 + row * 20, paint);
                }
                canvas.DrawText("Citation [1]. Physical examinaiton (quoted source).", left, 410,
                    SKTextAlign.Left, font, paint);
            }
            canvas.DrawRect(35, 450, 200, 100, paint);
            canvas.DrawText("Figure 1: native source figure", 35, 570, SKTextAlign.Left, font, paint);
            document.EndPage();
        }
        document.Close();
        return output.ToArray();
    }
}

public sealed class ReviewerLibreOfficeFactAttribute : FactAttribute
{
    public ReviewerLibreOfficeFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("EMF_REVIEWER_LAYOUT_TESTS") != "true")
            Skip = "Set EMF_REVIEWER_LAYOUT_TESTS=true to test synthetic pagination with installed LibreOffice.";
    }
}
