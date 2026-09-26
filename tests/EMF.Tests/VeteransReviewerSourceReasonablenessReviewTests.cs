using System.Text;
using DocumentFormat.OpenXml.Packaging;
using EMF.Core.Models;
using EMF.Extensions.VeteransClaims.Models;
using EMF.Extensions.VeteransClaims.Orchestration;
using static EMF.Tests.VeteransReviewerEvidencePresentationTests;

namespace EMF.Tests;

public sealed class VeteransReviewerSourceReasonablenessReviewTests
{
    private static readonly DateOnly ReviewDate = new(2026, 9, 26);

    [Theory]
    [InlineData("Weight loss of about 8200 pounds after his bariatric surgery.")]
    [InlineData("After his bariatric surgery he lost about 8,200 pounds.")]
    [InlineData("Weight loss of about\n8200 pounds was documented.")]
    [InlineData("Weight: 8200 lb")]
    [InlineData("Wt: 3700 kg")]
    public void Review_FlagsExtremeWeightWithoutGuessingCorrection(string text)
    {
        var finding = Assert.Single(Review(text));
        Assert.Equal("ExtremeWeightMagnitude", finding.Rule);
        Assert.Contains("no corrected value", finding.Message);
        Assert.Equal(text, finding.SourceText);
    }

    [Theory]
    [InlineData("Weight loss of about 82 pounds after his bariatric surgery.")]
    [InlineData("Weight loss of about 82.00 pounds after his bariatric surgery.")]
    [InlineData("He weighs 350 pounds.")]
    [InlineData("Cumulative medication dispensed: 8200 mg.")]
    [InlineData("AHI: 150. Improvement relative to baseline: 150%.")]
    [InlineData("PAP compliance: 100%; SpO2: 95%; mask fit of 60%.")]
    public void Review_DoesNotFlagOrdinaryValuesOrUnboundedPercentChanges(string text) => Assert.Empty(Review(text));

    [Theory]
    [InlineData("DATE OF NOTE: APR 17, 2027@09:39")]
    [InlineData("Signed: 04/17/2027 09:40")]
    [InlineData("Date of birth: April 12, 2056")]
    [InlineData("Specimen collected: 2027-01-02")]
    [InlineData("Follow-up note dated January 2, 2027")]
    [InlineData("Appointment completed on 01/02/2027")]
    public void Review_FlagsFutureDocumentedDates(string text)
    {
        var finding = Assert.Single(Review(text));
        Assert.Equal("FutureRecordedDate", finding.Rule);
        Assert.Contains("2026-09-26", finding.Message);
        Assert.Equal(text, finding.SourceText);
    }

    [Theory]
    [InlineData("Next appointment: 01/02/2027")]
    [InlineData("Appointment scheduled:\nJanuary 2, 2027")]
    [InlineData("Follow-up on 2027-01-02")]
    [InlineData("Planned surgery on 01/02/2027")]
    [InlineData("Surgery will be performed on 01/02/2027")]
    [InlineData("Patient is scheduled to be admitted on 01/02/2027")]
    [InlineData("Medication expiration: 01/02/2027")]
    [InlineData("Signed: 09/26/2026")]
    [InlineData("Signed: 01/02/2024; next appointment 01/02/2027")]
    public void Review_AllowsPlannedDatesAndDatesOnOrBeforeReviewDate(string text) => Assert.Empty(Review(text));

    [Fact]
    public void Review_AppointmentExceptionDoesNotHideFutureSignatureInSameLine()
    {
        var finding = Assert.Single(Review("Appointment: 01/02/2027; Signed: 01/03/2027"));
        Assert.Equal("01/03/2027", finding.Value);
    }

    [Theory]
    [InlineData("Signed: 02/30/2024")]
    [InlineData("Appointment: 2027-13-02")]
    [InlineData("Date entered: February 29, 2025")]
    public void Review_FlagsInvalidCalendarDatesEvenForAppointments(string text) =>
        Assert.Equal("InvalidCalendarDate", Assert.Single(Review(text)).Rule);

    [Theory]
    [InlineData("SpO2: 950%")]
    [InlineData("PAP compliance: 120%")]
    [InlineData("Mask fit: -5%")]
    public void Review_FlagsOnlyExplicitlyBoundedPercentages(string text) =>
        Assert.Equal("PercentageOutsideBounds", Assert.Single(Review(text)).Rule);

    [Fact]
    public void Review_UsesSelectedNativeGeometryInsteadOfConflictingExtractedText()
    {
        using var fixture = new VeteransReviewerNativeEvidencePageTests.NativePage(1224, 1584);
        fixture.Line("Weight loss of about 8200 pounds after his bariatric surgery.", 50, size: 12, x: 45);
        var page = fixture.Page(pageNumber: 2015);
        var original = page.Content.ToArray();
        var content = Content("Weight loss of about 82 pounds.", [page]);
        var finding = Assert.Single(VeteransReviewerSourceReasonablenessReview.Review(Details([content]), ReviewDate));
        Assert.Equal(2015, finding.SourcePage);
        Assert.Equal("8200 pounds", finding.Value);
        Assert.Equal(original, page.Content.ToArray());
    }

    [Fact]
    public void Review_DoesNotUseUnselectedOrUnrelatedEvidence()
    {
        var content = Content("Weight loss of about 8200 pounds.", [TextPage(1, "Weight: 82 pounds."), TextPage(2, "Weight: 8200 pounds.")]);
        var details = Details([content]);
        var member = details.PackageDetails.Artifacts[0];
        // Use the same selection contract consumed by native rendering.
        var selected = new EMF.Extensions.VeteransClaims.Models.Adjudication.EvidencePackageArtifact {
            EvidencePackageId = member.EvidencePackageId, ArtifactId = member.ArtifactId,
            ContentRole = member.ContentRole, ReviewerPageSelection = "1"
        };
        var scoped = new VeteransReviewerPackageDetails {
            PackageDetails = new() { Package = details.PackageDetails.Package, Artifacts = [selected] },
            Artifacts = details.Artifacts, ArtifactContents = [content]
        };
        Assert.Empty(VeteransReviewerSourceReasonablenessReview.Review(scoped, ReviewDate));
        var literature = new VeteransReviewerArtifactContent {
            Artifact = content.Artifact, Text = "Weight: 8200 pounds.", Appendix = VeteransReviewerPackageAppendix.MedicalLiterature
        };
        Assert.Empty(VeteransReviewerSourceReasonablenessReview.Review(Details([literature]), ReviewDate));
    }

    [Fact]
    public void Review_ChecksFutureMetadataWithoutTreatingFollowUpNoteAsScheduledAppointment()
    {
        var content = Content("Clinical findings.");
        ((Dictionary<string, object>)content.Artifact.Metadata)[VeteransArtifactMetadataKeys.NoteDate] = "2027-01-02";
        ((Dictionary<string, object>)content.Artifact.Metadata)[VeteransArtifactMetadataKeys.EvidenceTitle] = "SLEEP MED FOLLOW-UP NOTE";
        Assert.Equal("FutureRecordedDate", Assert.Single(VeteransReviewerSourceReasonablenessReview.Review(Details([content]), ReviewDate)).Rule);
    }

    [Fact]
    public void Review_IsDeterministicForExplicitReviewDate()
    {
        var details = Details([Content("Signed: 01/02/2027")]);
        Assert.Single(VeteransReviewerSourceReasonablenessReview.Review(details, ReviewDate));
        Assert.Empty(VeteransReviewerSourceReasonablenessReview.Review(details, new DateOnly(2027, 1, 2)));
        Assert.Single(VeteransReviewerSourceReasonablenessReview.Review(details, ReviewDate));
    }

    [Fact]
    public void Review_DoesNotTreatInaccessibleImageTextAsCheckedExtraction()
    {
        var content = Content("Weight: 8200 pounds.", [new() { PageNumber = 1, ContentType = "image/png", Content = new byte[] { 1 } }]);
        Assert.Empty(VeteransReviewerSourceReasonablenessReview.Review(Details([content]), ReviewDate));
    }

    [Fact]
    public void Review_HandlesUnparseablyLargeMagnitudeWithoutFailingPackageExport()
    {
        Assert.Equal("ExtremeWeightMagnitude", Assert.Single(Review("Weight: " + new string('9', 60) + " pounds.")).Rule);
    }

    [Fact]
    public void Render_DisplaysWarningBesideAffectedEvidenceAndPreservesOriginalValue()
    {
        const string source = "Weight loss of about 8200 pounds after his bariatric surgery.";
        var bytes = VeteransReviewerPackageDocxRenderer.Render(Details([Content(source, [TextPage(1, source)])]), sourceReviewDate: ReviewDate);
        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        var text = doc.MainDocumentPart!.Document!.Body!.InnerText;
        var warning = text.IndexOf("Source Data Warning — Review Required", StringComparison.Ordinal);
        Assert.True(warning >= 0);
        Assert.True(text.LastIndexOf(source, StringComparison.Ordinal) >
            text.IndexOf("no corrected value has been inferred", StringComparison.Ordinal));
        Assert.Contains("Implausible human weight magnitude", text);
        Assert.Contains(source, text);
        Assert.DoesNotContain("82 pounds", text);
    }

    [Fact]
    public void Render_UnrelatedClarificationDoesNotHideUnreviewedMagnitude()
    {
        const string source = "Weight loss of about 8200 pounds after his bariatric surgery.";
        var content = Content(source);
        var basis = Details([content]);
        var details = new VeteransReviewerPackageDetails {
            PackageDetails = basis.PackageDetails, Artifacts = basis.Artifacts, ArtifactContents = basis.ArtifactContents,
            SourceClarifications = [new() {
                ReviewerArtifactId = content.Artifact.Id, SourceLocator = "Clinical note", OriginalText = source,
                Clarification = "The appointment location was confirmed separately."
            }]
        };
        using var doc = WordprocessingDocument.Open(new MemoryStream(
            VeteransReviewerPackageDocxRenderer.Render(details, sourceReviewDate: ReviewDate)), false);
        Assert.Contains("Source Data Warning — Review Required", doc.MainDocumentPart!.Document!.Body!.InnerText);
    }

    private static IReadOnlyList<VeteransReviewerSourceCheckFinding> Review(string text) =>
        VeteransReviewerSourceReasonablenessReview.Review(Details([Content(text)]), ReviewDate);

    private static VeteransReviewerArtifactContent Content(string text, IReadOnlyList<PrintableArtifactPage>? pages = null) => new() {
        Artifact = new() { Id = new("source-check-note"), Name = "Clinical note", ArtifactType = "medical-record", Metadata = new Dictionary<string, object>() },
        Text = text, Appendix = VeteransReviewerPackageAppendix.MedicalEvidence, PrintablePages = pages ?? []
    };

    private static PrintableArtifactPage TextPage(int number, string text) => new() {
        PageNumber = number, ContentType = "text/plain", Content = Encoding.UTF8.GetBytes(text)
    };
}
