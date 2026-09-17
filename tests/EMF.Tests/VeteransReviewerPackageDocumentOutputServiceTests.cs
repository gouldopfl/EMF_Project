using DocumentFormat.OpenXml.Packaging;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.Tests;

public sealed class VeteransReviewerPackageDocumentOutputServiceTests
{
    [Fact]
    public async Task RenderAsync_Docx_DoesNotRequireConverter()
    {
        var output =
            await new VeteransReviewerPackageDocumentOutputService()
                .RenderAsync(
                    CreateDetails(),
                    VeteransReviewerPackageOutputFormat.Docx);

        Assert.NotNull(output.Docx);
        Assert.Null(output.Pdf);
        Assert.True(output.Docx!.Length > 4);
        Assert.Equal((byte)'P', output.Docx[0]);
        Assert.Equal((byte)'K', output.Docx[1]);
    }

    [Fact]
    public async Task RenderAsync_Pdf_ConvertsCanonicalDocx()
    {
        var converter = new RecordingConverter();

        var output =
            await new VeteransReviewerPackageDocumentOutputService(
                    converter)
                .RenderAsync(
                    CreateDetails(),
                    VeteransReviewerPackageOutputFormat.Pdf);

        Assert.Null(output.Docx);
        Assert.NotNull(output.Pdf);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(output.Pdf!, 0, 5));
        Assert.NotNull(converter.Docx);
        Assert.Equal((byte)'P', converter.Docx![0]);
        Assert.Equal((byte)'K', converter.Docx[1]);
    }

    [Fact]
    public async Task RenderAsync_Both_ReturnsSameDocxUsedForPdfConversion()
    {
        var converter = new RecordingConverter();

        var output =
            await new VeteransReviewerPackageDocumentOutputService(
                    converter)
                .RenderAsync(
                    CreateDetails(),
                    VeteransReviewerPackageOutputFormat.Both);

        Assert.NotNull(output.Docx);
        Assert.NotNull(output.Pdf);
        Assert.Equal(output.Docx, converter.Docx);
    }

    [Fact]
    public async Task RenderAsync_Pdf_RequiresConverter()
    {
        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => new VeteransReviewerPackageDocumentOutputService()
                    .RenderAsync(
                        CreateDetails(),
                        VeteransReviewerPackageOutputFormat.Pdf));

        Assert.Contains(
            "requires a DOCX-to-PDF converter",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderAsync_RejectsInvalidPdfConversionOutput()
    {
        var converter =
            new RecordingConverter(
                System.Text.Encoding.UTF8.GetBytes("not-a-pdf"));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new VeteransReviewerPackageDocumentOutputService(
                    converter)
                .RenderAsync(
                    CreateDetails(),
                    VeteransReviewerPackageOutputFormat.Pdf));
    }


    [Fact]
    public async Task RenderAsync_RegulatoryCitations_RequireProvider()
    {
        var details = CreateDetailsWithRegulatoryCitation();

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => new VeteransReviewerPackageDocumentOutputService()
                    .RenderAsync(
                        details,
                        VeteransReviewerPackageOutputFormat.Docx));

        Assert.Contains(
            "no regulatory text provider is configured",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderAsync_RegulatoryCitations_RenderCurrentEcfrText()
    {
        var details = CreateDetailsWithRegulatoryCitation();
        var provider = new RecordingRegulatoryTextProvider();

        var output =
            await new VeteransReviewerPackageDocumentOutputService(
                    regulatoryTextProvider: provider)
                .RenderAsync(
                    details,
                    VeteransReviewerPackageOutputFormat.Docx);

        Assert.NotNull(output.Docx);
        Assert.Equal(1, provider.CallCount);

        using var stream = new MemoryStream(output.Docx!);
        using var document = WordprocessingDocument.Open(stream, false);

        var text =
            document.MainDocumentPart!
                .Document!
                .InnerText;

        Assert.Contains(
            "Applicable VA Regulation",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "38 C.F.R. § 3.310(a)",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "Current authoritative regulation text.",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "current through September 15, 2026",
            text,
            StringComparison.Ordinal);
    }

    private static VeteransReviewerPackageDetails CreateDetails()
    {
        var packageId = new EvidencePackageId("package-output-1");

        return new VeteransReviewerPackageDetails
        {
            PackageDetails =
                new EvidencePackageDetails
                {
                    Package =
                        new EvidencePackage
                        {
                            Id = packageId,
                            ClaimIssueId = new ClaimIssueId("issue-output-1"),
                            Purpose = "Physician reviewer package",
                            ReviewerRole = "MedicalProfessional"
                        },
                    Artifacts = []
                },
            Artifacts = []
        };
    }


    private static VeteransReviewerPackageDetails CreateDetailsWithRegulatoryCitation()
    {
        var details = CreateDetails();

        return new VeteransReviewerPackageDetails
        {
            PackageDetails = details.PackageDetails,
            Artifacts = details.Artifacts,
            MedicalOpinionRequested =
                new VeteransReviewerMedicalOpinionRequest
                {
                    OpinionText = "Provide the requested medical opinion.",
                    ApplicableRegulatoryCitations =
                    [
                        "38 C.F.R. § 3.310(a)"
                    ]
                }
        };
    }

    private sealed class RecordingRegulatoryTextProvider :
        IVeteransReviewerRegulatoryTextProvider
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<VeteransReviewerApplicableRegulation>>
            GetCurrentAsync(
                IReadOnlyList<string> citations,
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;

            Assert.Single(citations);
            Assert.Equal(
                "38 C.F.R. § 3.310(a)",
                citations[0]);

            IReadOnlyList<VeteransReviewerApplicableRegulation> result =
            [
                new VeteransReviewerApplicableRegulation
                {
                    Citation = "38 C.F.R. § 3.310(a)",
                    Text = "(a) Current authoritative regulation text.",
                    SourceUri =
                        "https://www.ecfr.gov/api/versioner/v1/full/" +
                        "2026-09-15/title-38.xml?part=3&section=3.310",
                    UpToDateAsOf = new DateOnly(2026, 9, 15),
                    RetrievedUtc =
                        new DateTimeOffset(
                            2026, 9, 17, 11, 0, 0, TimeSpan.Zero),
                    SourceSha256 = new string('a', 64)
                }
            ];

            return Task.FromResult(result);
        }
    }

    private sealed class RecordingConverter :
        IVeteransReviewerPackageDocumentConverter
    {
        private readonly byte[] _pdf;

        public RecordingConverter(byte[]? pdf = null)
        {
            _pdf =
                pdf ??
                System.Text.Encoding.ASCII.GetBytes(
                    "%PDF-1.7\n%%EOF\n");
        }

        public byte[]? Docx { get; private set; }

        public Task<byte[]> ConvertDocxToPdfAsync(
            ReadOnlyMemory<byte> docx,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Docx = docx.ToArray();
            return Task.FromResult(_pdf);
        }
    }
}
