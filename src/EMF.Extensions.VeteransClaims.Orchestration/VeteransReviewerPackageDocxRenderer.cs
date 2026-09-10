using System.Buffers.Binary;
using System.Text;
using DocumentFormat.OpenXml;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public static class VeteransReviewerPackageDocxRenderer
{
    public static byte[] Render(
        VeteransReviewerPackageDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        var package =
            details.PackageDetails.Package;

        foreach (var packageArtifact in
            details.PackageDetails.Artifacts)
        {
            if (details.ArtifactContents.Any(
                content =>
                    content.Artifact.Id ==
                        packageArtifact.ArtifactId))
            {
                continue;
            }

            throw new InvalidOperationException(
                $"Evidence package '{package.Id.Value}' has no reviewable " +
                $"content for artifact '{packageArtifact.ArtifactId.Value}'.");
        }

        using var stream =
            new MemoryStream();

        using (var document =
            WordprocessingDocument.Create(
                stream,
                WordprocessingDocumentType.Document))
        {
            var mainPart =
                document.AddMainDocumentPart();

            var body =
                new Body(
                    StyledParagraph(
                        "Veterans Evidence Reviewer Package",
                        "Title"),
                    StyledParagraph(
                        $"Package: {package.Id.Value}",
                        "Subtitle"),
                    StyledParagraph(
                        $"Claim Issue: {package.ClaimIssueId.Value}",
                        "Subtitle"),
                    StyledParagraph(
                        $"Purpose: {package.Purpose}",
                        "Subtitle"),
                    StyledParagraph(
                        $"Reviewer Role: {package.ReviewerRole}",
                        "Subtitle"));

            AppendRoleSection(
                mainPart,
                body,
                details,
                EvidencePackageContentRoles
                    .GeneratedOrganizationalMaterial,
                "Generated Organizational Material");

            AppendRoleSection(
                mainPart,
                body,
                details,
                EvidencePackageContentRoles
                    .UnderlyingEvidence,
                "Underlying Evidence");

            body.Append(
                new SectionProperties(
                    new PageMargin
                    {
                        Top = 1440,
                        Right = 1440U,
                        Bottom = 1440,
                        Left = 1440U
                    }));

            mainPart.Document =
                new Document(body);
        }

        return stream.ToArray();
    }

    private static void AppendRoleSection(
        MainDocumentPart mainPart,
        Body body,
        VeteransReviewerPackageDetails details,
        string contentRole,
        string heading)
    {
        var contents =
            details.ArtifactContents
                .Where(
                    content =>
                        details.PackageDetails.Artifacts.Any(
                            packageArtifact =>
                                packageArtifact.ArtifactId ==
                                    content.Artifact.Id &&
                                string.Equals(
                                    packageArtifact.ContentRole,
                                    contentRole,
                                    StringComparison.Ordinal)))
                .ToArray();

        if (contents.Length == 0)
            return;

        var sectionHeading =
            StyledParagraph(
                heading,
                "Heading1");

        if (string.Equals(
                contentRole,
                EvidencePackageContentRoles.UnderlyingEvidence,
                StringComparison.Ordinal) &&
            details.ArtifactContents.Any(
                content =>
                    details.PackageDetails.Artifacts.Any(
                        packageArtifact =>
                            packageArtifact.ArtifactId ==
                                content.Artifact.Id &&
                            string.Equals(
                                packageArtifact.ContentRole,
                                EvidencePackageContentRoles
                                    .GeneratedOrganizationalMaterial,
                                StringComparison.Ordinal))))
        {
            sectionHeading.ParagraphProperties!.Append(
                new PageBreakBefore());
        }

        body.Append(sectionHeading);

        if (string.Equals(
                contentRole,
                EvidencePackageContentRoles.UnderlyingEvidence,
                StringComparison.Ordinal) &&
            contents.Any(content => content.Appendix is not null))
        {
            foreach (var group in
                contents
                    .Where(content => content.Appendix is not null)
                    .GroupBy(content => content.Appendix!)
                    .OrderBy(group => AppendixOrder(group.Key)))
            {
                body.Append(
                    StyledParagraph(
                        AppendixHeading(group.Key),
                        "Heading2"));

                AppendContents(
                    mainPart,
                    body,
                    group,
                    contentRole);
            }

            AppendContents(
                mainPart,
                body,
                contents.Where(content => content.Appendix is null),
                contentRole);

            return;
        }

        AppendContents(
            mainPart,
            body,
            contents,
            contentRole);
    }

    private static void AppendContents(
        MainDocumentPart mainPart,
        Body body,
        IEnumerable<VeteransReviewerArtifactContent> contents,
        string contentRole)
    {
        foreach (var content in contents)
        {
            body.Append(
                StyledParagraph(
                    $"Artifact Content: {content.Artifact.Name} " +
                    $"[{content.Artifact.Id.Value}] " +
                    $"[{contentRole}]",
                    "Heading2"));

            body.Append(
                ContentParagraph(
                    $"Artifact Type: {content.Artifact.ArtifactType}"));

            body.Append(
                ContentParagraph(
                    $"Created UTC: {content.Artifact.CreatedUtc:O}"));

            if (content.Artifact.Fingerprint is not null)
            {
                body.Append(
                    ContentParagraph(
                        $"Fingerprint: " +
                        $"{content.Artifact.Fingerprint.Algorithm} " +
                        $"{content.Artifact.Fingerprint.Value}"));
            }

            AppendMetadata(
                body,
                content.Artifact.Metadata,
                EMF.Extensions.VeteransClaims.Models
                    .VeteransArtifactMetadataKeys.SourceStartPage,
                "Source Start Page");

            AppendMetadata(
                body,
                content.Artifact.Metadata,
                EMF.Extensions.VeteransClaims.Models
                    .VeteransArtifactMetadataKeys.SourceEndPage,
                "Source End Page");

            AppendMetadata(
                body,
                content.Artifact.Metadata,
                EMF.Extensions.VeteransClaims.Models
                    .VeteransArtifactMetadataKeys.NoteDate,
                "Note Date");

            AppendMetadata(
                body,
                content.Artifact.Metadata,
                EMF.Extensions.VeteransClaims.Models
                    .VeteransArtifactMetadataKeys.NoteTitle,
                "Note Title");

            foreach (var provenance in content.Provenance)
            {
                body.Append(
                    ContentParagraph(
                        $"Provenance: {provenance.Source} | " +
                        $"{provenance.RecordedBy} | " +
                        $"{provenance.RecordedUtc:O}"));
            }

            foreach (var provenance in content.Provenance)
            {
                if (!string.Equals(
                        provenance.Source,
                        "EMF.Intelligence",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                body.Append(
                    ContentParagraph(
                        $"Promoted By: {provenance.RecordedBy}"));

                body.Append(
                    ContentParagraph(
                        $"Promoted UTC: {provenance.RecordedUtc:O}"));

                if (provenance.Properties.TryGetValue(
                        "reviewedBy",
                        out var reviewedBy) &&
                    !string.IsNullOrWhiteSpace(reviewedBy?.ToString()))
                {
                    body.Append(
                        ContentParagraph(
                            $"Reviewed By: {reviewedBy}"));
                }

                if (provenance.Properties.TryGetValue(
                        "reviewedUtc",
                        out var reviewedUtc) &&
                    DateTimeOffset.TryParse(
                        reviewedUtc?.ToString(),
                        out var reviewedAt))
                {
                    body.Append(
                        ContentParagraph(
                            $"Reviewed UTC: {reviewedAt:O}"));
                }
            }

            foreach (var relationship in content.Relationships)
            {
                body.Append(
                    ContentParagraph(
                        $"Relationship: " +
                        $"{relationship.SourceArtifactId.Value} -> " +
                        $"{relationship.TargetArtifactId.Value} | " +
                        $"{relationship.RelationshipType} | " +
                        $"{relationship.CreatedUtc:O}"));
            }

            if (content.PrintablePages.Count > 0)
            {
                AppendPrintablePages(
                    mainPart,
                    body,
                    content.PrintablePages);

                if (!string.IsNullOrWhiteSpace(content.Text))
                {
                    body.Append(
                        ContentParagraph(
                            "Extracted Text (Derived):"));

                    body.Append(
                        ContentParagraph(content.Text));
                }
            }
            else if (!string.IsNullOrWhiteSpace(content.Text))
            {
                body.Append(
                    ContentParagraph(content.Text));
            }
        }
    }

    private static int AppendixOrder(string appendix) =>
        appendix switch
        {
            VeteransReviewerPackageAppendix.MedicalEvidence => 0,
            VeteransReviewerPackageAppendix.ServiceRecords => 1,
            VeteransReviewerPackageAppendix.LayEvidence => 2,
            VeteransReviewerPackageAppendix.AdjudicativeRecords => 3,
            VeteransReviewerPackageAppendix.MedicalLiterature => 4,
            _ => int.MaxValue
        };

    private static string AppendixHeading(string appendix) =>
        appendix switch
        {
            VeteransReviewerPackageAppendix.MedicalEvidence =>
                "Appendix A — Medical Evidence",
            VeteransReviewerPackageAppendix.ServiceRecords =>
                "Appendix B — Service Records",
            VeteransReviewerPackageAppendix.LayEvidence =>
                "Appendix C — Lay Evidence",
            VeteransReviewerPackageAppendix.AdjudicativeRecords =>
                "Appendix D — Adjudicative Records",
            VeteransReviewerPackageAppendix.MedicalLiterature =>
                "Appendix E — Medical / Scientific Literature",
            _ => appendix
        };

    private static Paragraph StyledParagraph(
        string text,
        string styleId)
    {
        var properties =
            new ParagraphProperties(
                new ParagraphStyleId
                {
                    Val = styleId
                });

        if (string.Equals(
                styleId,
                "Title",
                StringComparison.Ordinal))
        {
            properties.Append(
                new SpacingBetweenLines
                {
                    After = "240"
                });
        }
        else if (string.Equals(
                     styleId,
                     "Heading1",
                     StringComparison.Ordinal))
        {
            properties.Append(
                new KeepNext());

            properties.Append(
                new SpacingBetweenLines
                {
                    Before = "120",
                    After = "120"
                });
        }
        else if (string.Equals(
                     styleId,
                     "Heading2",
                     StringComparison.Ordinal))
        {
            properties.Append(
                new KeepNext());

            properties.Append(
                new SpacingBetweenLines
                {
                    Before = "120",
                    After = "60"
                });
        }
        else if (string.Equals(
                     styleId,
                     "Subtitle",
                     StringComparison.Ordinal))
        {
            properties.Append(
                new SpacingBetweenLines
                {
                    After = "40"
                });
        }

        return new Paragraph(
            properties,
            new Run(
                new Text(SanitizeXmlText(text))
                {
                    Space = SpaceProcessingModeValues.Preserve
                }));
    }

    private static void AppendMetadata(
        Body body,
        IReadOnlyDictionary<string, object> metadata,
        string key,
        string label)
    {
        if (!metadata.TryGetValue(key, out var value))
            return;

        var text = value?.ToString();

        if (string.IsNullOrWhiteSpace(text))
            return;

        body.Append(
            ContentParagraph(
                $"{label}: {text}"));
    }

    private static Paragraph ContentParagraph(
        string text)
    {
        var paragraph =
            new Paragraph(
                new ParagraphProperties(
                    new SpacingBetweenLines
                    {
                        After = "60"
                    }));

        var lines =
            text.Replace(
                    "\r\n",
                    "\n",
                    StringComparison.Ordinal)
                .Replace(
                    '\r',
                    '\n')
                .Split('\n');

        for (var index = 0;
             index < lines.Length;
             index++)
        {
            if (index > 0)
            {
                paragraph.Append(
                    new Run(
                        new Break()));
            }

            paragraph.Append(
                new Run(
                    new Text(SanitizeXmlText(lines[index]))
                    {
                        Space =
                            SpaceProcessingModeValues.Preserve
                    }));
        }

        return paragraph;
    }

    private static void AppendPrintablePages(
        MainDocumentPart mainPart,
        Body body,
        IReadOnlyList<EMF.Core.Models.PrintableArtifactPage> pages)
    {
        var expectedPageNumber = 1;

        foreach (var page in pages)
        {
            if (page.PageNumber != expectedPageNumber)
                throw new InvalidOperationException(
                    "Printable artifact pages are not in sequential order.");

            if (string.Equals(
                    page.ContentType,
                    "text/plain",
                    StringComparison.OrdinalIgnoreCase))
            {
                body.Append(ContentParagraph($"Source Page {page.PageNumber}"));
                body.Append(ContentParagraph(DecodePrintableText(page.Content)));
                expectedPageNumber++;
                continue;
            }

            if (!string.Equals(
                    page.ContentType,
                    "image/png",
                    StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException(
                    $"Unsupported printable page content type '{page.ContentType}'.");

            var (width, height) =
                GetPngDimensions(page.Content);

            var imagePart =
                mainPart.AddImagePart(
                    ImagePartType.Png);

            using (var stream =
                new MemoryStream(
                    page.Content.ToArray(),
                    writable: false))
            {
                imagePart.FeedData(stream);
            }

            var relationshipId =
                mainPart.GetIdOfPart(imagePart);

            var (cx, cy) =
                FitPageToDocument(width, height);

            var drawingId =
                checked((uint)mainPart.ImageParts.Count());

            body.Append(
                ContentParagraph(
                    $"Source Page {page.PageNumber}"));

            body.Append(
                ImageParagraph(
                    relationshipId,
                    drawingId,
                    page.PageNumber,
                    cx,
                    cy));

            expectedPageNumber++;
        }
    }

    private static string DecodePrintableText(ReadOnlyMemory<byte> content)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(content.Span).TrimStart('\uFEFF');
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException(
                "Printable text content is not valid UTF-8.", ex);
        }
    }

    private static (uint Width, uint Height) GetPngDimensions(
        ReadOnlyMemory<byte> content)
    {
        ReadOnlySpan<byte> pngHeader =
        [
            0x89, 0x50, 0x4E, 0x47,
            0x0D, 0x0A, 0x1A, 0x0A
        ];

        if (content.Length < 24 ||
            !content.Span[..8].SequenceEqual(pngHeader))
            throw new InvalidDataException(
                "Printable page is not a valid PNG image.");

        var width =
            BinaryPrimitives.ReadUInt32BigEndian(
                content.Span.Slice(16, 4));

        var height =
            BinaryPrimitives.ReadUInt32BigEndian(
                content.Span.Slice(20, 4));

        if (width == 0 || height == 0)
            throw new InvalidDataException(
                "Printable PNG page has invalid dimensions.");

        return (width, height);
    }

    private static (long Cx, long Cy) FitPageToDocument(
        uint width,
        uint height)
    {
        const long maxWidth = 5_943_600;
        const long maxHeight = 7_772_400;

        var scale =
            Math.Min(
                maxWidth / (double)width,
                maxHeight / (double)height);

        return (
            checked((long)Math.Round(width * scale)),
            checked((long)Math.Round(height * scale)));
    }

    private static Paragraph ImageParagraph(
        string relationshipId,
        uint drawingId,
        int pageNumber,
        long cx,
        long cy) =>
        new(
            new ParagraphProperties(
                new Justification
                {
                    Val = JustificationValues.Center
                }),
            new Run(
                new Drawing(
                    new DW.Inline(
                        new DW.Extent
                        {
                            Cx = cx,
                            Cy = cy
                        },
                        new DW.DocProperties
                        {
                            Id = drawingId,
                            Name = $"Source Page {pageNumber}"
                        },
                        new DW.NonVisualGraphicFrameDrawingProperties(
                            new A.GraphicFrameLocks
                            {
                                NoChangeAspect = true
                            }),
                        new A.Graphic(
                            new A.GraphicData(
                                new PIC.Picture(
                                    new PIC.NonVisualPictureProperties(
                                        new PIC.NonVisualDrawingProperties
                                        {
                                            Id = 0U,
                                            Name = $"Source Page {pageNumber}.png"
                                        },
                                        new PIC.NonVisualPictureDrawingProperties()),
                                    new PIC.BlipFill(
                                        new A.Blip
                                        {
                                            Embed = relationshipId
                                        },
                                        new A.Stretch(
                                            new A.FillRectangle())),
                                    new PIC.ShapeProperties(
                                        new A.Transform2D(
                                            new A.Offset
                                            {
                                                X = 0L,
                                                Y = 0L
                                            },
                                            new A.Extents
                                            {
                                                Cx = cx,
                                                Cy = cy
                                            }),
                                        new A.PresetGeometry(
                                            new A.AdjustValueList())
                                        {
                                            Preset =
                                                A.ShapeTypeValues.Rectangle
                                        })))
                            {
                                Uri =
                                    "http://schemas.openxmlformats.org/" +
                                    "drawingml/2006/picture"
                            })))));

    private static string SanitizeXmlText(string text)
    {
        var sanitized = new StringBuilder(text.Length);

        foreach (var rune in text.EnumerateRunes())
        {
            var value = rune.Value;

            if (value is 0x9 or 0xA or 0xD ||
                value is >= 0x20 and <= 0xD7FF ||
                value is >= 0xE000 and <= 0xFFFD ||
                value is >= 0x10000 and <= 0x10FFFF)
            {
                sanitized.Append(rune.ToString());
            }
            else
            {
                sanitized.Append('\uFFFD');
            }
        }

        return sanitized.ToString();
    }

    private static Paragraph Paragraph(
        string text) =>
        new(
            new Run(
                new Text(SanitizeXmlText(text))
                {
                    Space = SpaceProcessingModeValues.Preserve
                }));
}
