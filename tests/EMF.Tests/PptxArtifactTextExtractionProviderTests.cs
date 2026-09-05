using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class PptxArtifactTextExtractionProviderTests
{
    [Fact]
    public async Task ExtractTextAsync_ReadsSlideText()
    {
        var content =
            CreatePresentation(
                "PPTX evidence text");

        var provider =
            new PptxArtifactTextExtractionProvider(
                new StubContentStore(content));

        var text =
            await provider.ExtractTextAsync(
                new ArtifactId("pptx-001"));

        Assert.Contains("PPTX evidence text", text);
    }

    [Theory]
    [InlineData(
        "package-entries",
        "PPTX package exceeds the maximum allowed entry count.")]
    [InlineData(
        "package-entry-size",
        "PPTX package entry exceeds the maximum allowed size.")]
    [InlineData(
        "package-total-size",
        "PPTX package exceeds the maximum allowed extracted size.")]
    [InlineData(
        "slides",
        "PPTX exceeds the maximum allowed slide count.")]
    [InlineData(
        "text-nodes",
        "PPTX exceeds the maximum allowed text-node count.")]
    [InlineData(
        "text",
        "PPTX extracted text exceeds the maximum allowed size.")]
    public async Task ExtractTextAsync_EnforcesResourceLimit(
        string limit,
        string expectedMessage)
    {
        var content =
            CreatePresentation(
                "First slide",
                "Second slide");

        var maxPackageEntryCount =
            PptxArtifactTextExtractionProvider
                .DefaultMaxPackageEntryCount;
        var maxPackageEntryBytes =
            PptxArtifactTextExtractionProvider
                .DefaultMaxPackageEntryBytes;
        var maxPackageTotalBytes =
            PptxArtifactTextExtractionProvider
                .DefaultMaxPackageTotalBytes;
        var maxSlideCount =
            PptxArtifactTextExtractionProvider
                .DefaultMaxSlideCount;
        var maxTextNodeCount =
            PptxArtifactTextExtractionProvider
                .DefaultMaxTextNodeCount;
        var maxExtractedTextChars =
            PptxArtifactTextExtractionProvider
                .DefaultMaxExtractedTextChars;

        switch (limit)
        {
            case "package-entries":
                maxPackageEntryCount = 1;
                break;
            case "package-entry-size":
                maxPackageEntryBytes = 1;
                break;
            case "package-total-size":
                maxPackageTotalBytes = 1;
                break;
            case "slides":
                maxSlideCount = 1;
                break;
            case "text-nodes":
                maxTextNodeCount = 1;
                break;
            case "text":
                maxExtractedTextChars = 1;
                break;
        }

        var provider =
            new PptxArtifactTextExtractionProvider(
                new StubContentStore(content),
                maxPackageEntryCount: maxPackageEntryCount,
                maxPackageEntryBytes: maxPackageEntryBytes,
                maxPackageTotalBytes: maxPackageTotalBytes,
                maxSlideCount: maxSlideCount,
                maxTextNodeCount: maxTextNodeCount,
                maxExtractedTextChars: maxExtractedTextChars);

        var ex =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => provider.ExtractTextAsync(
                    new ArtifactId($"pptx-{limit}")));

        Assert.Equal(expectedMessage, ex.Message);
    }

    private static byte[] CreatePresentation(
        params string[] slideTexts)
    {
        using var stream = new MemoryStream();

        using (var doc =
            PresentationDocument.Create(
                stream,
                PresentationDocumentType.Presentation))
        {
            var part = doc.AddPresentationPart();

            part.Presentation =
                new Presentation(
                    new SlideIdList());

            for (var index = 0;
                 index < slideTexts.Length;
                 index++)
            {
                var slidePart =
                    part.AddNewPart<SlidePart>();

                slidePart.Slide =
                    new Slide(
                        new CommonSlideData(
                            new ShapeTree(
                                new NonVisualGroupShapeProperties(),
                                new GroupShapeProperties(),
                                new Shape(
                                    new NonVisualShapeProperties(),
                                    new ShapeProperties(),
                                    new TextBody(
                                        new A.BodyProperties(),
                                        new A.ListStyle(),
                                        new A.Paragraph(
                                            new A.Run(
                                                new A.Text(
                                                    slideTexts[index]))))))));

                slidePart.Slide.Save();

                part.Presentation.SlideIdList!.Append(
                    new SlideId
                    {
                        Id = 256U + (uint)index,
                        RelationshipId =
                            part.GetIdOfPart(slidePart)
                    });
            }

            part.Presentation.Save();
        }

        return stream.ToArray();
    }

    private sealed class StubContentStore(byte[] content) :
        IArtifactContentStore
    {
        public Task<byte[]?> ReadAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<byte[]?>(content);

        public Task WriteAsync(
            ArtifactId artifactId,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
