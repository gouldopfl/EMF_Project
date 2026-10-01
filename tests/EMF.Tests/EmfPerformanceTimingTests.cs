using System.Text.RegularExpressions;
using EMF.Common;
using EMF.ConsoleApplication;
using EMF.Core.Models;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Orchestration.Services;
using SkiaSharp;

namespace EMF.Tests;

[Collection(ReviewerDeploymentEnvironmentCollection.Name)]
public sealed class EmfPerformanceTimingTests
{
    private sealed class Setting : IDisposable
    {
        private readonly string? _previous = Environment.GetEnvironmentVariable("EMF_PERF_TIMING");
        internal Setting(string? value) => Environment.SetEnvironmentVariable("EMF_PERF_TIMING", value);
        public void Dispose() => Environment.SetEnvironmentVariable("EMF_PERF_TIMING", _previous);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("true")]
    [InlineData("01")]
    public void TimingIsSilentUnlessSettingIsExactlyOne(string? setting)
    {
        using var environment = new Setting(setting);
        var output = new List<string>();
        using (EmfPerformanceTiming.BeginReviewer(output.Add))
        {
            using var timing = EmfPerformanceTiming.Measure(EmfPerformancePhase.DocxConstruction);
            EmfPerformanceTiming.Count(EmfPerformanceCounter.PaddleRuns, 4);
        }
        Assert.Empty(output);
    }

    [Fact]
    public async Task EnabledTimingAggregatesAcrossAwaitsAndDoesNotDuplicateNestedSummaries()
    {
        using var environment = new Setting("1");
        var output = new List<string>();
        using (EmfPerformanceTiming.BeginReviewer(output.Add))
        {
            using (EmfPerformanceTiming.BeginReviewer(_ => throw new Exception("Nested summary")))
            {
                await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() =>
                {
                    using var timing = EmfPerformanceTiming.Measure(EmfPerformancePhase.PaddleRegionDetection);
                    EmfPerformanceTiming.Count(EmfPerformanceCounter.PrivacyOcrPages);
                    EmfPerformanceTiming.Count(EmfPerformanceCounter.PaddleRuns, 4);
                    timing.Dispose(); // Idempotence: automatic disposal must not count twice.
                })));
            }
        }
        var summary = Assert.Single(output);
        Assert.Contains("Nested/overlapping measurements; do not sum rows.", summary);
        AssertCounter(summary, "Privacy OCR pages", 12);
        AssertCounter(summary, "Paddle runs", 48);
        AssertCalls(summary, "Paddle region detection", 12);
        foreach (var label in new[] { "Reviewer total", "Evidence loading", "Evidence extraction/materialization",
            "Presentation/version preparation", "DOCX construction", "DOCX save", "PDF conversion total",
            "Privacy masking", "Paddle raster decode", "OCR 0 degrees", "OCR 90 degrees", "OCR 180 degrees",
            "OCR 270 degrees", "Source-page raster/render", "Enlargement candidate detection",
            "Enlargement crop/render pipeline", "LibreOffice conversion", "Converter total" })
            Assert.Contains(label, summary);
        output.Clear();
        using (EmfPerformanceTiming.BeginReviewer(output.Add)) { }
        AssertCounter(Assert.Single(output), "Paddle runs", 0);
    }

    [Fact]
    public void TimingDoesNotChangeCanonicalPackageOutputOrLeakSourceValues()
    {
        var snapshot = VeteransReviewerPackageDeterminismTests.Fixture(
            "Synthetic sensitive medical condition", false, "PRIVATE_PAGE_TEXT", 3, false);
        var settings = new VeteransReviewerPackageRenderSettings(new(2026, 9, 29));
        byte[] baseline;
        using (new Setting(null))
            baseline = VeteransReviewerPackagePresentationPreparation.Prepare(snapshot, settings).MaterializeDocx();
        using var environment = new Setting("1");
        var output = new List<string>();
        byte[] measured;
        using (EmfPerformanceTiming.BeginReviewer(output.Add))
            measured = VeteransReviewerPackagePresentationPreparation.Prepare(snapshot, settings).MaterializeDocx();
        Assert.Equal(baseline, measured);
        var summary = Assert.Single(output);
        foreach (var sensitive in new[] { "Example Veteran", "Example Preparer", "PRIVATE_PAGE_TEXT",
            "Synthetic sensitive medical condition", "SOURCE_SENTINEL", "Synthetic source", snapshot.PackageId.Value })
            Assert.DoesNotContain(sensitive, summary);
        AssertCalls(summary, "Presentation/version preparation", 1);
        AssertCalls(summary, "DOCX construction", 1);
        AssertCalls(summary, "DOCX save", 1);
        // All summary data lines are fixed labels followed only by numeric durations/counters.
        foreach (var line in summary.Split('\n').Skip(2).SkipLast(1).Where(line => line.Length > 0 && !line.StartsWith("Inclusive diagnostics") && !line.StartsWith("Raster metrics")))
            Assert.Matches(@"^[A-Za-z0-9 /()\-]+\s+\d+(?:\.\d+ s; calls \d+)?$", line.TrimEnd('\r'));
    }

    [Fact]
    public void PrivacyOcrCountsFourPassesPerRasterWithoutAddingInference()
    {
        using var environment = new Setting("1");
        using var bitmap = new SKBitmap(100, 100);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var bytes = data.ToArray();
        var output = new List<string>();
        using (EmfPerformanceTiming.BeginReviewer(output.Add))
            for (var i = 0; i < 2; i++)
                Assert.Empty(PaddleImageTextRegionDetector.Detect(bytes));
        var summary = Assert.Single(output);
        AssertCounter(summary, "Privacy OCR pages", 2);
        AssertCounter(summary, "Paddle runs", 8);
        AssertCounter(summary, "Raster OCR requests", 2);
        AssertCounter(summary, "Unique raster identities available", 1);
        AssertCounter(summary, "Repeat raster requests", 1);
        AssertCounter(summary, "Potential Paddle runs avoided by exact-raster caching", 4);
        AssertCalls(summary, "Paddle raster decode", 2);
        foreach (var label in new[] { "OCR 0 degrees", "OCR 90 degrees", "OCR 180 degrees", "OCR 270 degrees" })
            AssertCalls(summary, label, 2);
    }

    [Fact]
    public void DiagnosticsFailureCannotFailTheOperation()
    {
        using var environment = new Setting("1");
        using (EmfPerformanceTiming.BeginReviewer(_ => throw new IOException("Sensitive sink error")))
        {
            using var timing = EmfPerformanceTiming.Measure(EmfPerformancePhase.DocxSave);
        }
    }

    [Fact]
    public async Task FailedConverterIsMeasuredWithoutLoggingItsPathsOrOutput()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var environment = new Setting("1");
        var directory = Directory.CreateTempSubdirectory("emf-perf-test-").FullName;
        var path = Path.Combine(directory, "private-patient-filename");
        try
        {
            await File.WriteAllTextAsync(path, "#!/bin/sh\necho 'PRIVATE_OCR_TEXT diagnosis veteran-name' >&2\nexit 7\n");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var output = new List<string>();
            using (EmfPerformanceTiming.BeginReviewer(output.Add))
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    new LibreOfficeVeteransReviewerPackageDocumentConverter(path)
                        .ConvertDocxToPdfAsync(new byte[] { 1, 2, 3 }));
            var summary = Assert.Single(output);
            AssertCalls(summary, "Converter total", 1);
            AssertCalls(summary, "LibreOffice conversion", 1);
            AssertCalls(summary, "Converter profile fingerprint", 1);
            foreach (var sensitive in new[] { directory, path, "PRIVATE_OCR_TEXT", "diagnosis", "veteran-name" })
                Assert.DoesNotContain(sensitive, summary);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void SupplementCountersAndCropOutputAreUnchanged()
    {
        using var fixture = new VeteransReviewerNativeEvidencePageTests.NativePage(612, 792, 612, 792);
        fixture.Line("Table 1. PRIVATE_MEDICAL_CAPTION", 140, x: 50);
        for (var row = 0; row < 5; row++) fixture.Line($"Group {row}  42  0.8", 155 + row * 14, x: 50);
        var page = fixture.Page(pageNumber: 1);
        IReadOnlyList<VeteransReviewerSourceEnlargement> baseline;
        using (new Setting(null))
            baseline = VeteransReviewerSourceEnlargements.Find(page, page.Content, 5_000_000, 6_000_000);
        using var environment = new Setting("1");
        var output = new List<string>();
        IReadOnlyList<VeteransReviewerSourceEnlargement> measured;
        using (EmfPerformanceTiming.BeginReviewer(output.Add))
            measured = VeteransReviewerSourceEnlargements.Find(page, page.Content, 5_000_000, 6_000_000);
        var expected = Assert.Single(baseline.Where(r => r.Selected));
        var actual = Assert.Single(measured.Where(r => r.Selected));
        Assert.Equal(expected.Bounds, actual.Bounds);
        Assert.Equal(expected.Magnification, actual.Magnification);
        Assert.Equal(expected.Content.ToArray(), actual.Content.ToArray());
        var summary = Assert.Single(output);
        AssertCounter(summary, "Supplements selected", 1);
        AssertCalls(summary, "Enlargement candidate detection", 1);
        Assert.DoesNotContain("PRIVATE_MEDICAL_CAPTION", summary);
    }

    [Fact]
    public void TopLevelRemainderUsesOnlyExclusiveIntervals()
    {
        using var environment = new Setting("1");
        var output = new List<string>();
        using (EmfPerformanceTiming.BeginReviewer(output.Add))
        {
            using (EmfPerformanceTiming.MeasureTopLevel(EmfPerformancePhase.PresentationPreparation))
            {
                using var nested = EmfPerformanceTiming.MeasureTopLevel(EmfPerformancePhase.DocxConstruction);
                using var privacy = EmfPerformanceTiming.Measure(EmfPerformancePhase.PrivacyMasking);
            }
            using (EmfPerformanceTiming.MeasureTopLevel(EmfPerformancePhase.PdfConversion)) { }
        }
        var summary = Assert.Single(output);
        AssertCalls(summary, "Top-level Presentation/version preparation", 1);
        AssertCalls(summary, "Top-level PDF conversion total", 1);
        Assert.DoesNotContain("Top-level DOCX construction", summary);
        AssertCalls(summary, "DOCX construction", 1);
        AssertCalls(summary, "Unattributed top-level remainder", 1);
        static double Seconds(string text, string label) => double.Parse(
            Regex.Match(text, $@"(?m)^{Regex.Escape(label)}\s+(\d+\.\d+) s").Groups[1].Value,
            System.Globalization.CultureInfo.InvariantCulture);
        var attributed = Seconds(summary, "Top-level Presentation/version preparation") +
            Seconds(summary, "Top-level PDF conversion total");
        Assert.InRange(Math.Abs(Seconds(summary, "Reviewer total") - attributed -
            Seconds(summary, "Unattributed top-level remainder")), 0, .003);
    }

    [Fact]
    public async Task ConcurrentIndependentTopLevelScopesSuppressTheRemainder()
    {
        using var environment = new Setting("1");
        var output = new List<string>();
        using (EmfPerformanceTiming.BeginReviewer(output.Add))
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var first = Task.Run(async () =>
            {
                using var timing = EmfPerformanceTiming.MeasureTopLevel(EmfPerformancePhase.EvidenceLoading);
                entered.SetResult();
                await release.Task;
            });
            await entered.Task;
            try
            {
                using var second = EmfPerformanceTiming.MeasureTopLevel(EmfPerformancePhase.DocxConstruction);
            }
            finally { release.SetResult(); await first; }
        }
        var summary = Assert.Single(output);
        Assert.Contains("Unattributed top-level remainder unavailable: concurrent/unfinished top-level scopes.", summary);
        Assert.DoesNotContain("Unattributed top-level remainder    ", summary);
    }

    [Fact]
    public void ChildOutlivingItsTopLevelOwnerSuppressesTheRemainder()
    {
        using var environment = new Setting("1");
        var output = new List<string>();
        using (EmfPerformanceTiming.BeginReviewer(output.Add))
        {
            var parent = EmfPerformanceTiming.MeasureTopLevel(EmfPerformancePhase.PresentationPreparation);
            var child = EmfPerformanceTiming.MeasureTopLevel(EmfPerformancePhase.DocxConstruction);
            parent.Dispose();
            child.Dispose();
        }
        Assert.Contains("Unattributed top-level remainder unavailable", Assert.Single(output));
    }

    [Fact]
    public void RasterIdentityCountsDistinguishBytesAndPriorSuccessfulRequests()
    {
        using var environment = new Setting("1");
        var output = new List<string>();
        using (EmfPerformanceTiming.BeginReviewer(output.Add))
        {
            var first = EmfPerformanceTiming.RecordPrivacyRaster(new byte[] { 1, 2, 3 });
            Assert.False(first.PreviouslyCompleted);
            Assert.False(EmfPerformanceTiming.RecordPrivacyRaster(new byte[] { 1, 2, 3 }).PreviouslyCompleted);
            first.Complete();
            Assert.True(EmfPerformanceTiming.RecordPrivacyRaster(new byte[] { 1, 2, 3 }).PreviouslyCompleted);
            Assert.False(EmfPerformanceTiming.RecordPrivacyRaster(new byte[] { 3, 2, 1 }).PreviouslyCompleted);
        }
        var summary = Assert.Single(output);
        AssertCounter(summary, "Raster OCR requests", 4);
        AssertCounter(summary, "Unique raster identities available", 2);
        AssertCounter(summary, "Repeat raster requests", 2);
        AssertCounter(summary, "Potential Paddle runs avoided by exact-raster caching", 0);
    }

    private static void AssertCounter(string summary, string label, int count) =>
        Assert.Matches($@"(?m)^{Regex.Escape(label)}\s+{count}\r?$", summary);

    private static void AssertCalls(string summary, string label, int count) =>
        Assert.Matches($@"(?m)^{Regex.Escape(label)}\s+\d+\.\d+ s; calls {count}\r?$", summary);
}
