using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace EMF.Extensions.VeteransClaims.Orchestration;

/// <summary>DOCX sections delegate actual pagination to Word/LibreOffice.</summary>
internal sealed class VeteransReviewerEvidenceSections
{
    private const string PackageHeader =
        "CONFIDENTIAL — VETERAN MEDICAL INFORMATION | Veterans Evidence Package for Medical Review";
    private readonly MainDocumentPart _mainPart;
    private readonly string _footerId;
    private readonly string _packageHeaderId;
    private string? _sourceFooterId;
    private string? _continuationHeaderId;
    private bool _sourcePage;
    private bool _landscape;

    internal const string EvidenceTitleColor = "365F91";

    internal const uint SourceSideMargin = 504; // 0.35 inches
    internal const int SourceTopMargin = 720; // compact two-line running header
    internal const int SourceBottomMargin = 432;

    public VeteransReviewerEvidenceSections(MainDocumentPart mainPart, string footerId)
    {
        _mainPart = mainPart;
        _footerId = footerId;
        _packageHeaderId = AddHeader(null);
    }

    private string SourceFooterId()
    {
        if (_sourceFooterId is not null) return _sourceFooterId;
        var footer = _mainPart.AddNewPart<FooterPart>();
        footer.Footer = new Footer(new Paragraph(
            new ParagraphProperties(new SpacingBetweenLines { Before = "0", After = "0" },
                new Justification { Val = JustificationValues.Right }),
            SmallRun("Page "), new SimpleField(SmallRun("1")) { Instruction = "PAGE" },
            SmallRun(" of "), new SimpleField(SmallRun("1")) { Instruction = "NUMPAGES" }));
        return _sourceFooterId = _mainPart.GetIdOfPart(footer);
    }

    public void Start(Body body, string? evidenceTitle, string titleColor = EvidenceTitleColor)
    {
        CloseSection(body);
        _sourcePage = false;
        _landscape = false;
        _continuationHeaderId = evidenceTitle is null
            ? null : AddHeader(evidenceTitle + " — Continued", titleColor);
    }

    public void StartSourcePage(Body body, int pageNumber, bool landscape, string? supplement = null)
    {
        CloseSection(body);
        _sourcePage = true;
        _landscape = landscape;
        var header = _mainPart.AddNewPart<HeaderPart>();
        header.Header = new Header(HeaderParagraph(PackageHeader, "16"),
            HeaderParagraph($"Appendix F — Medical Literature | Source Page {pageNumber}" +
                (supplement is null ? "" : $" | {supplement} enlargement (supplement)"), "16"));
        _continuationHeaderId = _mainPart.GetIdOfPart(header);
    }

    private void CloseSection(Body body)
    {
        // A section mark on its own paragraph can overflow an otherwise full
        // page and create a header-only continuation. Reuse the final body
        // paragraph when possible; tables still require a paragraph after them.
        if (body.LastChild is Paragraph last &&
            last.ParagraphProperties?.GetFirstChild<SectionProperties>() is null)
        {
            last.ParagraphProperties ??= new ParagraphProperties();
            last.ParagraphProperties.AddChild(CurrentProperties(), true);
        }
        else
            body.Append(new Paragraph(new ParagraphProperties(
                new SpacingBetweenLines { Before = "0", After = "0", Line = "20",
                    LineRule = LineSpacingRuleValues.Exact },
                CurrentProperties())));
    }

    public SectionProperties CurrentProperties()
    {
        var properties = new SectionProperties(
            new HeaderReference { Type = HeaderFooterValues.Default,
                Id = _continuationHeaderId ?? _packageHeaderId });
        if (_continuationHeaderId is not null && !_sourcePage)
            properties.Append(new HeaderReference { Type = HeaderFooterValues.First, Id = _packageHeaderId });
        properties.Append(new FooterReference { Type = HeaderFooterValues.Default,
            Id = _sourcePage ? SourceFooterId() : _footerId });
        if (_continuationHeaderId is not null && !_sourcePage)
            properties.Append(new FooterReference { Type = HeaderFooterValues.First, Id = _footerId });
        properties.Append(
            new SectionType { Val = SectionMarkValues.NextPage },
            new PageSize { Width = _landscape ? 15840U : 12240U,
                Height = _landscape ? 12240U : 15840U,
                Orient = _landscape ? PageOrientationValues.Landscape : PageOrientationValues.Portrait },
            _sourcePage
                ? new PageMargin { Top = SourceTopMargin, Right = SourceSideMargin, Bottom = SourceBottomMargin,
                    Left = SourceSideMargin, Header = 180U, Footer = 180U }
                : new PageMargin { Top = 1440, Right = 1440U, Bottom = 1440,
                    Left = 1440U, Header = 360U, Footer = 720U });
        if (_continuationHeaderId is not null && !_sourcePage)
            properties.Append(new TitlePage());
        // Deliberately no PageNumberType.Start: PAGE/NUMPAGES span the package.
        return properties;
    }

    private string AddHeader(string? continuation, string titleColor = EvidenceTitleColor)
    {
        var part = _mainPart.AddNewPart<HeaderPart>();
        part.Header = new Header(HeaderParagraph(PackageHeader, "16"));
        if (continuation is not null)
            part.Header.Append(HeaderParagraph(continuation, "20", titleColor));
        return _mainPart.GetIdOfPart(part);
    }

    private static Paragraph HeaderParagraph(string text, string size, string? color = null)
    {
        var properties = new RunProperties(
            new RunFonts { Ascii = VeteransReviewerFonts.Body, HighAnsi = VeteransReviewerFonts.Body }, new Bold());
        if (color is not null)
            properties.Append(new Color { Val = color });
        properties.Append(new FontSize { Val = size });
        return new Paragraph(
            new ParagraphProperties(new SpacingBetweenLines { Before = "0", After = "40" }),
            new Run(properties,
                new Text(text) { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve }));
    }

    private static Run SmallRun(string text) => new(
        new RunProperties(new RunFonts { Ascii = VeteransReviewerFonts.Body, HighAnsi = VeteransReviewerFonts.Body }, new FontSize { Val = "16" }),
        new Text(text) { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve });
}
