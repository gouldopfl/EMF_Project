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
        using var stream = new MemoryStream();

        using (var doc = PresentationDocument.Create(
            stream,
            PresentationDocumentType.Presentation))
        {
            var part = doc.AddPresentationPart();
            part.Presentation = new Presentation(new SlideIdList());

            var slidePart = part.AddNewPart<SlidePart>();
            slidePart.Slide = new Slide(
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
                                        new A.Text("PPTX evidence text"))))))));

            slidePart.Slide.Save();

            part.Presentation.SlideIdList!.Append(
                new SlideId
                {
                    Id = 256U,
                    RelationshipId = part.GetIdOfPart(slidePart)
                });

            part.Presentation.Save();
        }

        var provider =
            new PptxArtifactTextExtractionProvider(
                new StubContentStore(stream.ToArray()));

        var text =
            await provider.ExtractTextAsync(
                new ArtifactId("pptx-001"));

        Assert.Contains("PPTX evidence text", text);
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
