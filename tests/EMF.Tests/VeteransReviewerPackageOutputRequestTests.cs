using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.ConsoleApplication;

namespace EMF.Tests;

public sealed class VeteransReviewerPackageOutputRequestTests
{
    [Fact]
    public void Resolve_PdfExtension_InfersPdf()
    {
        var path =
            Path.Combine(
                Path.GetTempPath(),
                $"reviewer-{Guid.NewGuid():N}.pdf");

        var request =
            VeteransReviewerPackageOutputRequestResolver.Resolve(path);

        Assert.Equal(VeteransReviewerPackageOutputFormat.Pdf, request.Format);
        Assert.Null(request.DocxPath);
        Assert.Equal(Path.GetFullPath(path), request.PdfPath);
    }

    [Fact]
    public void Resolve_DocxExtension_InfersDocx()
    {
        var path =
            Path.Combine(
                Path.GetTempPath(),
                $"reviewer-{Guid.NewGuid():N}.docx");

        var request =
            VeteransReviewerPackageOutputRequestResolver.Resolve(path);

        Assert.Equal(VeteransReviewerPackageOutputFormat.Docx, request.Format);
        Assert.Equal(Path.GetFullPath(path), request.DocxPath);
        Assert.Null(request.PdfPath);
    }

    [Fact]
    public void Resolve_Both_CreatesSiblingDocxAndPdfPaths()
    {
        var stem =
            Path.Combine(
                Path.GetTempPath(),
                $"reviewer-{Guid.NewGuid():N}");

        var request =
            VeteransReviewerPackageOutputRequestResolver.Resolve(
                stem + ".docx",
                "both");

        Assert.Equal(VeteransReviewerPackageOutputFormat.Both, request.Format);
        Assert.Equal(Path.GetFullPath(stem + ".docx"), request.DocxPath);
        Assert.Equal(Path.GetFullPath(stem + ".pdf"), request.PdfPath);
    }

    [Fact]
    public void Resolve_RejectsConflictingExplicitFormatAndExtension()
    {
        var path =
            Path.Combine(
                Path.GetTempPath(),
                $"reviewer-{Guid.NewGuid():N}.docx");

        Assert.Throws<InvalidOperationException>(
            () => VeteransReviewerPackageOutputRequestResolver.Resolve(
                path,
                "pdf"));
    }

    [Fact]
    public void Parser_AcceptsBasisFormatAndOutputInEitherOptionOrder()
    {
        var output =
            Path.Combine(
                Path.GetTempPath(),
                $"reviewer-{Guid.NewGuid():N}.docx");

        var parsed =
            VeteransReviewerPackageCommandOptionsParser.TryParse(
                [
                    "evidence",
                    "reviewer",
                    "/tmp/claim.db",
                    "issue-1",
                    "--format",
                    "both",
                    "--basis",
                    "basis-1",
                    output
                ],
                out var options,
                out var error);

        Assert.True(parsed, error);
        Assert.NotNull(options);
        Assert.Equal("basis-1", options!.BasisId);
        Assert.Equal(VeteransReviewerPackageOutputFormat.Both, options.Output!.Format);
        Assert.NotNull(options.Output.DocxPath);
        Assert.NotNull(options.Output.PdfPath);
    }

    [Fact]
    public void Parser_RejectsFormatWithoutOutputPath()
    {
        var parsed =
            VeteransReviewerPackageCommandOptionsParser.TryParse(
                [
                    "evidence",
                    "reviewer",
                    "/tmp/claim.db",
                    "issue-1",
                    "--format",
                    "pdf"
                ],
                out _,
                out var error);

        Assert.False(parsed);
        Assert.Contains("requires an output path", error, StringComparison.Ordinal);
    }
}
