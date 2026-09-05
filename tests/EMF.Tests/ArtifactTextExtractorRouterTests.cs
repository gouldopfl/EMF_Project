using System.Reflection;
using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class ArtifactTextExtractorRouterTests
{
    [Fact]
    public async Task ExtractTextAsync_ReturnsNullWhenArtifactMissing()
    {
        var router =
            new ArtifactTextExtractorRouter(
                new InMemoryEvidenceRepository(),
                new DefaultArtifactContentTypeResolver(),
                []);

        var result =
            await router.ExtractTextAsync(
                new ArtifactId("missing"));

        Assert.Null(result);
    }

    [Fact]
    public async Task ExtractTextAsync_RejectsDifferentReturnedArtifact()
    {
        var requested = new ArtifactId("artifact-001");
        var returned = CreateArtifact("artifact-other", ".txt");

        var repository =
            Proxy<IEvidenceRepository>(
                (method, args) =>
                    method.Name == "GetArtifactAsync"
                        ? Task.FromResult<Artifact?>(returned)
                        : throw new NotSupportedException());

        var router =
            new ArtifactTextExtractorRouter(
                repository,
                new DefaultArtifactContentTypeResolver(),
                [new StubProvider("text/plain", "wrong")]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => router.ExtractTextAsync(requested));
    }

    [Fact]
    public async Task ExtractTextAsync_ReturnsNullWhenContentTypeUnknown()
    {
        var repository =
            new InMemoryEvidenceRepository();

        var artifact =
            CreateArtifact(
                "artifact-001",
                ".bin");

        await repository.AddArtifactAsync(artifact);

        var router =
            new ArtifactTextExtractorRouter(
                repository,
                new DefaultArtifactContentTypeResolver(),
                []);

        Assert.Null(
            await router.ExtractTextAsync(artifact.Id));
    }

    [Fact]
    public async Task ExtractTextAsync_UsesMatchingProvider()
    {
        var repository =
            new InMemoryEvidenceRepository();

        var artifact =
            CreateArtifact(
                "artifact-002",
                ".txt");

        await repository.AddArtifactAsync(artifact);

        var provider =
            new StubProvider(
                "text/plain",
                "recognized text");

        var router =
            new ArtifactTextExtractorRouter(
                repository,
                new DefaultArtifactContentTypeResolver(),
                [provider]);

        Assert.Equal(
            "recognized text",
            await router.ExtractTextAsync(artifact.Id));
    }

    [Fact]
    public async Task ExtractTextAsync_RoutesRtfToRtfProvider()
    {
        var repository =
            new InMemoryEvidenceRepository();

        var artifact =
            CreateArtifact(
                "artifact-rtf-001",
                ".rtf");

        await repository.AddArtifactAsync(artifact);

        const string rtf =
            @"{\rtf1\ansi " +
            @"EMF RTF Evidence Test\par " +
            @"MRI confirms lumbar changes.}";

        var router =
            new ArtifactTextExtractorRouter(
                repository,
                new DefaultArtifactContentTypeResolver(),
                [
                    new RtfArtifactTextExtractionProvider(
                        new StubContentStore(
                            System.Text.Encoding.ASCII.GetBytes(rtf)))
                ]);

        var text =
            await router.ExtractTextAsync(artifact.Id);

        Assert.NotNull(text);
        Assert.Contains(
            "EMF RTF Evidence Test",
            text);
        Assert.Contains(
            "MRI confirms lumbar changes.",
            text);
    }

    [Fact]
    public async Task ExtractTextAsync_RoutesOdtToOpenDocumentProvider()
    {
        var repository = new InMemoryEvidenceRepository();
        var artifact = CreateArtifact("artifact-odt-001", ".odt");
        await repository.AddArtifactAsync(artifact);

        var content = System.IO.File.ReadAllBytes(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestData",
                "evidence-sample.odt"));

        var router =
            new ArtifactTextExtractorRouter(
                repository,
                new DefaultArtifactContentTypeResolver(),
                [
                    new OpenDocumentArtifactTextExtractionProvider(
                        new StubContentStore(content),
                        "application/vnd.oasis.opendocument.text",
                        "evidence.odt")
                ]);

        var text =
            await router.ExtractTextAsync(artifact.Id);

        Assert.NotNull(text);
    }

    [Fact]
    public async Task ExtractTextAsync_RoutesHtmlToHtmlProvider()
    {
        var repository = new InMemoryEvidenceRepository();
        var artifact = CreateArtifact("artifact-html-001", ".html");
        await repository.AddArtifactAsync(artifact);

        const string html =
            "<html><body><p>HTML evidence text</p></body></html>";

        var router =
            new ArtifactTextExtractorRouter(
                repository,
                new DefaultArtifactContentTypeResolver(),
                [
                    new HtmlArtifactTextExtractionProvider(
                        new StubContentStore(
                            System.Text.Encoding.UTF8.GetBytes(html)))
                ]);

        var text =
            await router.ExtractTextAsync(artifact.Id);

        Assert.Contains("HTML evidence text", text);
    }

    [Fact]
    public async Task ExtractTextAsync_RoutesPdfToPdfProvider()
    {
        var repository =
            new InMemoryEvidenceRepository();

        var artifact =
            CreateArtifact(
                "artifact-pdf-001",
                ".pdf");

        await repository.AddArtifactAsync(artifact);

        var pdfPath =
            Path.Combine(
                AppContext.BaseDirectory,
                "TestData",
                "evidence-sample.pdf");

        var content =
            await File.ReadAllBytesAsync(pdfPath);

        var router =
            new ArtifactTextExtractorRouter(
                repository,
                new DefaultArtifactContentTypeResolver(),
                [
                    new PdfArtifactTextExtractionProvider(
                        new StubContentStore(content))
                ]);

        var text =
            await router.ExtractTextAsync(artifact.Id);

        Assert.NotNull(text);
        Assert.Contains(
            "Veteran has chronic instability.",
            text);
        Assert.Contains(
            "MRI confirms lumbar degenerative changes.",
            text);
    }

    [Fact]
    public async Task ExtractTextAsync_RoutesXlsxToXlsxProvider()
    {
        var repository =
            new InMemoryEvidenceRepository();

        var artifact =
            CreateArtifact(
                "artifact-xlsx-001",
                ".xlsx");

        await repository.AddArtifactAsync(artifact);

        var xlsxPath =
            Path.Combine(
                AppContext.BaseDirectory,
                "TestData",
                "evidence-sample.xlsx");

        var content =
            await File.ReadAllBytesAsync(xlsxPath);

        var router =
            new ArtifactTextExtractorRouter(
                repository,
                new DefaultArtifactContentTypeResolver(),
                [
                    new XlsxArtifactTextExtractionProvider(
                        new StubContentStore(content))
                ]);

        var text =
            await router.ExtractTextAsync(artifact.Id);

        Assert.NotNull(text);
        Assert.Contains(
            "[Worksheet: Evidence]",
            text);
        Assert.Contains(
            "B1: MRI confirms lumbar changes.",
            text);
        Assert.Contains(
            "C1: Shared string evidence value",
            text);
        Assert.Contains(
            "[Worksheet: Medications]",
            text);
        Assert.Contains(
            "B1: Pantoprazole",
            text);
    }

    [Fact]
    public async Task ExtractTextAsync_RoutesXlsToXlsProvider()
    {
        var repository =
            new InMemoryEvidenceRepository();

        var artifact =
            CreateArtifact(
                "artifact-xls-001",
                ".xls");

        await repository.AddArtifactAsync(artifact);

        var xlsPath =
            Path.Combine(
                AppContext.BaseDirectory,
                "TestData",
                "evidence-sample.xls");

        var content =
            await File.ReadAllBytesAsync(xlsPath);

        var router =
            new ArtifactTextExtractorRouter(
                repository,
                new DefaultArtifactContentTypeResolver(),
                [
                    new XlsArtifactTextExtractionProvider(
                        new StubContentStore(content))
                ]);

        var text =
            await router.ExtractTextAsync(artifact.Id);

        Assert.NotNull(text);
        Assert.Contains(
            "ATTENDANCE (Pg 5)",
            text);
        Assert.Contains(
            "125 Strange, Bill",
            text);
        Assert.Contains(
            "148 Wright, T.J.",
            text);
        Assert.Contains(
            "Total in Attendance",
            text);
    }

    [Fact]
    public async Task ExtractTextAsync_RoutesEmlToEmlProvider()
    {
        var repository =
            new InMemoryEvidenceRepository();

        var artifact =
            CreateArtifact(
                "artifact-eml-001",
                ".eml");

        await repository.AddArtifactAsync(artifact);

        var content =
            "From: sender@example.com\r\n" +
            "To: recipient@example.com\r\n" +
            "Subject: Evidence\r\n" +
            "\r\n" +
            "Veteran has chronic instability.";

        var router =
            new ArtifactTextExtractorRouter(
                repository,
                new DefaultArtifactContentTypeResolver(),
                [
                    new EmlArtifactTextExtractionProvider(
                        new StubContentStore(
                            System.Text.Encoding.UTF8.GetBytes(content)))
                ]);

        var text =
            await router.ExtractTextAsync(artifact.Id);

        Assert.Equal(
            "Veteran has chronic instability.",
            text);
    }


    [Theory]
    [InlineData(".csv", "text/csv", "a,b")]
    [InlineData(".json", "application/json", "{\"status\":\"ready\"}")]
    [InlineData(".xml", "application/xml", "<status>ready</status>")]
    public async Task ExtractTextAsync_RoutesUtf8Formats(
        string extension,
        string contentType,
        string expected)
    {
        var repository = new InMemoryEvidenceRepository();
        var artifact = CreateArtifact("artifact-utf8", extension);
        await repository.AddArtifactAsync(artifact);

        IArtifactTextExtractionProvider provider =
            contentType switch
            {
                "text/csv" =>
                    new CsvArtifactTextExtractionProvider(
                        new StubContentStore(System.Text.Encoding.UTF8.GetBytes(expected))),
                "application/json" =>
                    new JsonArtifactTextExtractionProvider(
                        new StubContentStore(System.Text.Encoding.UTF8.GetBytes(expected))),
                _ =>
                    new XmlArtifactTextExtractionProvider(
                        new StubContentStore(System.Text.Encoding.UTF8.GetBytes(expected)))
            };

        var router =
            new ArtifactTextExtractorRouter(
                repository,
                new DefaultArtifactContentTypeResolver(),
                [provider]);

        Assert.Equal(
            expected,
            await router.ExtractTextAsync(artifact.Id));
    }


    [Fact]
    public async Task ExtractTextAsync_RoutesPptxToPptxProvider()
    {
        var repository = new InMemoryEvidenceRepository();
        var artifact = CreateArtifact("artifact-pptx-001", ".pptx");
        await repository.AddArtifactAsync(artifact);

        using var stream = new MemoryStream();

        using (var doc =
            DocumentFormat.OpenXml.Packaging.PresentationDocument.Create(
                stream,
                DocumentFormat.OpenXml.PresentationDocumentType.Presentation))
        {
            var part = doc.AddPresentationPart();
            part.Presentation =
                new DocumentFormat.OpenXml.Presentation.Presentation(
                    new DocumentFormat.OpenXml.Presentation.SlideIdList());

            var slidePart = part.AddNewPart<
                DocumentFormat.OpenXml.Packaging.SlidePart>();

            slidePart.Slide =
                new DocumentFormat.OpenXml.Presentation.Slide(
                    new DocumentFormat.OpenXml.Presentation.CommonSlideData(
                        new DocumentFormat.OpenXml.Presentation.ShapeTree(
                            new DocumentFormat.OpenXml.Presentation.NonVisualGroupShapeProperties(),
                            new DocumentFormat.OpenXml.Presentation.GroupShapeProperties(),
                            new DocumentFormat.OpenXml.Presentation.Shape(
                                new DocumentFormat.OpenXml.Presentation.NonVisualShapeProperties(),
                                new DocumentFormat.OpenXml.Presentation.ShapeProperties(),
                                new DocumentFormat.OpenXml.Presentation.TextBody(
                                    new DocumentFormat.OpenXml.Drawing.BodyProperties(),
                                    new DocumentFormat.OpenXml.Drawing.ListStyle(),
                                    new DocumentFormat.OpenXml.Drawing.Paragraph(
                                        new DocumentFormat.OpenXml.Drawing.Run(
                                            new DocumentFormat.OpenXml.Drawing.Text(
                                                "PPTX routed evidence"))))))));

            slidePart.Slide.Save();

            part.Presentation.SlideIdList!.Append(
                new DocumentFormat.OpenXml.Presentation.SlideId
                {
                    Id = 256U,
                    RelationshipId = part.GetIdOfPart(slidePart)
                });

            part.Presentation.Save();
        }

        var router = new ArtifactTextExtractorRouter(
            repository,
            new DefaultArtifactContentTypeResolver(),
            [new PptxArtifactTextExtractionProvider(
                new StubContentStore(stream.ToArray()))]);

        var text = await router.ExtractTextAsync(artifact.Id);

        Assert.Contains("PPTX routed evidence", text);
    }

    [Fact]
    public async Task ExtractTextAsync_RoutesPptToPptProvider()
    {
        var repository = new InMemoryEvidenceRepository();
        var artifact = CreateArtifact("artifact-ppt-001", ".ppt");
        await repository.AddArtifactAsync(artifact);

        var path = Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "evidence-sample.ppt");

        var content = await File.ReadAllBytesAsync(path);

        var router = new ArtifactTextExtractorRouter(
            repository,
            new DefaultArtifactContentTypeResolver(),
            [new PptArtifactTextExtractionProvider(
                new StubContentStore(content))]);

        var text = await router.ExtractTextAsync(artifact.Id);

        Assert.Contains(
            "EMF Legacy PowerPoint Evidence Test",
            text);
    }

    [Fact]
    public async Task ExtractTextAsync_RoutesDocxToDocxProvider()
    {
        var repository = new InMemoryEvidenceRepository();
        var artifact = CreateArtifact("artifact-docx-001", ".docx");
        await repository.AddArtifactAsync(artifact);

        using var stream = new MemoryStream();

        using (var document =
            DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Create(
                stream,
                DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var part = document.AddMainDocumentPart();

            part.Document =
                new DocumentFormat.OpenXml.Wordprocessing.Document(
                    new DocumentFormat.OpenXml.Wordprocessing.Body(
                        new DocumentFormat.OpenXml.Wordprocessing.Paragraph(
                            new DocumentFormat.OpenXml.Wordprocessing.Run(
                                new DocumentFormat.OpenXml.Wordprocessing.Text(
                                    "Required evidence")))));
        }

        var router =
            new ArtifactTextExtractorRouter(
                repository,
                new DefaultArtifactContentTypeResolver(),
                [
                    new DocxArtifactTextExtractionProvider(
                        new StubContentStore(stream.ToArray()))
                ]);

        var text =
            await router.ExtractTextAsync(artifact.Id);

        Assert.Contains("Required evidence", text);
    }


    [Fact]
    public async Task ExtractTextAsync_RoutesDocToDocProvider()
    {
        var repository =
            new InMemoryEvidenceRepository();

        var artifact =
            CreateArtifact(
                "artifact-doc-001",
                ".doc");

        await repository.AddArtifactAsync(artifact);

        var path =
            Path.Combine(
                AppContext.BaseDirectory,
                "TestData",
                "evidence-sample.doc");

        var content =
            await File.ReadAllBytesAsync(path);

        var router =
            new ArtifactTextExtractorRouter(
                repository,
                new DefaultArtifactContentTypeResolver(),
                [
                    new DocArtifactTextExtractionProvider(
                        new StubContentStore(content))
                ]);

        var text =
            await router.ExtractTextAsync(artifact.Id);

        Assert.NotNull(text);
        Assert.Contains(
            "VA-Blue-Button-report-Michael-Gould-7-30-2026_1225pm(5).pdf",
            text);
        Assert.Contains(
            "Excellent. We now have the two cornerstone documents",
            text);
    }


    [Fact]
    public async Task ExtractTextAsync_RoutesMsgToMsgProvider()
    {
        var repository = new InMemoryEvidenceRepository();
        var artifact = CreateArtifact("artifact-msg-001", ".msg");
        await repository.AddArtifactAsync(artifact);

        var path =
            Path.Combine(
                AppContext.BaseDirectory,
                "TestData",
                "TxtSampleEmail.msg");

        var content =
            await File.ReadAllBytesAsync(path);

        var router =
            new ArtifactTextExtractorRouter(
                repository,
                new DefaultArtifactContentTypeResolver(),
                [
                    new MsgArtifactTextExtractionProvider(
                        new StubContentStore(content),
                        new OutlookMessageDecoder())
                ]);

        var text =
            await router.ExtractTextAsync(artifact.Id);

        Assert.False(string.IsNullOrWhiteSpace(text));
    }


    [Fact]
    public async Task ExtractTextAsync_RoutesPngToImageProvider()
    {
        var repository =
            new InMemoryEvidenceRepository();

        var artifact =
            CreateArtifact(
                "artifact-image-001",
                ".png");

        await repository.AddArtifactAsync(artifact);

        var router =
            new ArtifactTextExtractorRouter(
                repository,
                new DefaultArtifactContentTypeResolver(),
                [
                    new ImageArtifactTextExtractionProvider(
                        new StubContentStore([1, 2, 3]),
                        new StubOcrService("recognized image text"))
                ]);

        var text =
            await router.ExtractTextAsync(artifact.Id);

        Assert.Equal(
            "recognized image text",
            text);
    }

    [Fact]
    public async Task ExtractTextAsync_ThrowsWhenKnownTypeUnsupported()
    {
        var repository =
            new InMemoryEvidenceRepository();

        var artifact =
            CreateArtifact(
                "artifact-003",
                ".pdf");

        await repository.AddArtifactAsync(artifact);

        var router =
            new ArtifactTextExtractorRouter(
                repository,
                new DefaultArtifactContentTypeResolver(),
                []);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => router.ExtractTextAsync(artifact.Id));
    }

    private static T Proxy<T>(
        Func<MethodInfo, object?[]?, object?> handler)
        where T : class
    {
        var proxy = DispatchProxy.Create<T, TestProxy>();
        ((TestProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private class TestProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?>? Handler
            { get; set; }

        protected override object? Invoke(
            MethodInfo? targetMethod,
            object?[]? args) =>
            Handler!(targetMethod!, args);
    }

    private static Artifact CreateArtifact(
        string id,
        string extension) =>
        new()
        {
            Id = new ArtifactId(id),
            Name = "evidence" + extension,
            ArtifactType = "file",
            Metadata = new Dictionary<string, object>
            {
                [ArtifactMetadataKeys.FileExtension] =
                    extension
            }
        };

    private sealed class StubContentStore :
        EMF.Core.Contracts.Storage.IArtifactContentStore
    {
        private readonly byte[] _content;

        public StubContentStore(byte[] content)
        {
            _content = content;
        }

        public Task<byte[]?> ReadAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<byte[]?>(_content);

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


    private sealed class StubOcrService :
        IImageOcrService
    {
        private readonly string _text;

        public StubOcrService(string text)
        {
            _text = text;
        }

        public Task<string?> RecognizeTextAsync(
            OcrRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(_text);
    }

    private sealed class StubProvider :
        IArtifactTextExtractionProvider
    {
        private readonly string _contentType;
        private readonly string _text;

        public StubProvider(
            string contentType,
            string text)
        {
            _contentType = contentType;
            _text = text;
        }

        public bool CanExtract(string contentType) =>
            string.Equals(
                contentType,
                _contentType,
                StringComparison.OrdinalIgnoreCase);

        public Task<string?> ExtractTextAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(_text);
    }
}
