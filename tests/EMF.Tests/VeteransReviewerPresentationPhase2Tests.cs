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
            .Where(section => section.GetFirstChild<TitlePage>() is not null).ToArray();
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
            Assert.Equal("Consolas", p.Descendants<RunFonts>().Single().Ascii!.Value);
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
            Assert.Equal(images[i], embedded[i]);
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
        Assert.Equal("Consolas", row.Descendants<RunFonts>().Single().Ascii!.Value);
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
        Assert.Equal("Consolas", row.Descendants<RunFonts>().Single().Ascii!.Value);
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
            var evidencePages = rendered.Where(text => text.Contains(title, StringComparison.Ordinal)
                && text.Contains("Source Page ", StringComparison.Ordinal)).ToArray();
            Assert.True(evidencePages.Length == 2, string.Join("\n---PAGE---\n", evidencePages));
            Assert.DoesNotContain("— Continued", evidencePages[0]);
            Assert.Contains("Source Page 1", evidencePages[0]);
            Assert.Contains(title + " — Continued", evidencePages[1]);
            Assert.Contains("Source Page 2", evidencePages[1]);
        }
        Assert.All(rendered, text => Assert.DoesNotContain("unused extracted text", text));
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
