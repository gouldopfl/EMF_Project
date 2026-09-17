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
