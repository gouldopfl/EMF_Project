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
    private string? _continuationHeaderId;

    public VeteransReviewerEvidenceSections(MainDocumentPart mainPart, string footerId)
    {
        _mainPart = mainPart;
        _footerId = footerId;
        _packageHeaderId = AddHeader(null);
    }

    public void Start(Body body, string? evidenceTitle)
    {
        body.Append(new Paragraph(new ParagraphProperties(
            new SpacingBetweenLines { Before = "0", After = "0", Line = "20",
                LineRule = LineSpacingRuleValues.Exact },
            CurrentProperties())));
        _continuationHeaderId = evidenceTitle is null
            ? null : AddHeader(evidenceTitle + " — Continued");
    }

    public SectionProperties CurrentProperties()
    {
        var properties = new SectionProperties(
            new HeaderReference { Type = HeaderFooterValues.Default,
                Id = _continuationHeaderId ?? _packageHeaderId });
        if (_continuationHeaderId is not null)
            properties.Append(new HeaderReference { Type = HeaderFooterValues.First, Id = _packageHeaderId });
        properties.Append(new FooterReference { Type = HeaderFooterValues.Default, Id = _footerId });
        if (_continuationHeaderId is not null)
            properties.Append(new FooterReference { Type = HeaderFooterValues.First, Id = _footerId });
        properties.Append(
            new SectionType { Val = SectionMarkValues.NextPage },
            new PageSize { Width = 12240U, Height = 15840U },
            new PageMargin { Top = 1440, Right = 1440U, Bottom = 1440,
                Left = 1440U, Header = 360U, Footer = 720U });
        if (_continuationHeaderId is not null)
            properties.Append(new TitlePage());
        // Deliberately no PageNumberType.Start: PAGE/NUMPAGES span the package.
        return properties;
    }

    private string AddHeader(string? continuation)
    {
        var part = _mainPart.AddNewPart<HeaderPart>();
        part.Header = new Header(HeaderParagraph(PackageHeader, "16"));
        if (continuation is not null)
            part.Header.Append(HeaderParagraph(continuation, "20"));
        return _mainPart.GetIdOfPart(part);
    }

    private static Paragraph HeaderParagraph(string text, string size) => new(
        new ParagraphProperties(new SpacingBetweenLines { Before = "0", After = "40" }),
        new Run(new RunProperties(new RunFonts { Ascii = "Cambria", HighAnsi = "Cambria" },
            new Bold(), new FontSize { Val = size }),
            new Text(text) { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve }));
}
