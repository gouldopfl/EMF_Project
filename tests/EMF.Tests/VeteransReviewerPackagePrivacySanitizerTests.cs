using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.Tests;

public sealed class VeteransReviewerPackagePrivacySanitizerTests
{
    [Fact]
    public void FinalDocumentSweep_ReplacesDuplicateIdentifierLinesWithoutChangingSource()
    {
        static DocumentFormat.OpenXml.Wordprocessing.Paragraph Paragraph(params string[] parts) => new(
            parts.Select(p => new DocumentFormat.OpenXml.Wordprocessing.Run(
                new DocumentFormat.OpenXml.Wordprocessing.Text(p))));
        var source = new DocumentFormat.OpenXml.Wordprocessing.Body(
            Paragraph("Patient name: Example"),
            Paragraph("MR", "N: 123-45-5668"),
            Paragraph("SSN: ", "123455668"),
            Paragraph("Findings unchanged."));
        var original = source.OuterXml;
        var reviewer = (DocumentFormat.OpenXml.Wordprocessing.Body)source.CloneNode(true);

        VeteransReviewerDocumentPrivacy.Mask(reviewer);

        Assert.Equal(original, source.OuterXml);
        Assert.Equal(new[] { "Patient name: Example", "Patient identifier: 5668", "Findings unchanged." },
            reviewer.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>().Select(p => p.InnerText));
        Assert.DoesNotContain("12345", reviewer.InnerText);
        Assert.DoesNotContain("MRN:", reviewer.InnerText);
        Assert.DoesNotContain("SSN:", reviewer.InnerText);
        var once = reviewer.OuterXml;
        VeteransReviewerDocumentPrivacy.Mask(reviewer);
        Assert.Equal(once, reviewer.OuterXml);
    }

    [Fact]
    public void FinalDocumentSweep_RetainsDifferentIdentifiersAndSurroundingNarrative()
    {
        var body = new DocumentFormat.OpenXml.Wordprocessing.Body(
            new[] { "MRN: 123-45-5668", "SSN: 123-45-6789", "Narrative SSN: 123-45-5668; finding retained." }
                .Select(t => new DocumentFormat.OpenXml.Wordprocessing.Paragraph(
                    new DocumentFormat.OpenXml.Wordprocessing.Run(new DocumentFormat.OpenXml.Wordprocessing.Text(t)))));
        VeteransReviewerDocumentPrivacy.Mask(body);
        Assert.Equal(new[] { "Patient identifier: 5668", "Patient identifier: 6789", "Narrative SSN: ***-**-5668; finding retained." },
            body.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>().Select(p => p.InnerText));
    }

    [Theory]
    [InlineData("123-45-5668")]
    [InlineData("MRN: 123455668")]
    [InlineData("SSN: 123 - 45 - 5668")]
    [InlineData("MRN: 123-45-5668")]
    [InlineData("Social Security Number: 123455668")]
    [InlineData("SSN: 123 45 5668")]
    [InlineData("In narrative (123-45-5668), the findings are unchanged.")]
    public void Redact_MasksSsnAcrossReviewerTextPaths(string original)
    {
        var result = VeteransReviewerPackagePrivacySanitizer.Redact(original);
        Assert.DoesNotContain("123-45-5668", result);
        Assert.DoesNotContain("123455668", result);
        Assert.DoesNotContain("123 45 5668", result);
        Assert.Contains("***-**-5668", result);
        Assert.Contains("123", original);
        Assert.Equal(result, VeteransReviewerPackagePrivacySanitizer.Redact(result));
    }

    [Fact]
    public void FinalDocumentSweep_MasksSsnSplitAcrossRunsAndLines()
    {
        var body = new DocumentFormat.OpenXml.Wordprocessing.Body(
            new DocumentFormat.OpenXml.Wordprocessing.Paragraph(new DocumentFormat.OpenXml.Wordprocessing.Run(
                new DocumentFormat.OpenXml.Wordprocessing.Text("SSN:"))),
            new DocumentFormat.OpenXml.Wordprocessing.Paragraph(
                new DocumentFormat.OpenXml.Wordprocessing.Run(new DocumentFormat.OpenXml.Wordprocessing.Text("123")),
                new DocumentFormat.OpenXml.Wordprocessing.Run(new DocumentFormat.OpenXml.Wordprocessing.Text("45")),
                new DocumentFormat.OpenXml.Wordprocessing.Run(new DocumentFormat.OpenXml.Wordprocessing.Text("5668; clinical finding unchanged."))));
        VeteransReviewerDocumentPrivacy.Mask(body);
        Assert.DoesNotContain("123455668", body.InnerText);
        Assert.Contains("***-**-5668; clinical finding unchanged.", body.InnerText);
    }

    [Fact]
    public void Redact_MasksVaFileNumber()
    {
        var result =
            VeteransReviewerPackagePrivacySanitizer.Redact(
                "VA File Number 123456789");

        Assert.Equal(
            "VA File Number *****6789",
            result);
    }

    [Fact]
    public void Redact_MasksFormattedSsn()
    {
        var result =
            VeteransReviewerPackagePrivacySanitizer.Redact(
                "SSN: 123-45-6789");

        Assert.Equal(
            "SSN: ***-**-6789",
            result);
    }

    [Fact]
    public void Redact_DoesNotMaskUnlabeledNineDigitNumber()
    {
        const string text =
            "Device serial 123456789";

        Assert.Equal(
            text,
            VeteransReviewerPackagePrivacySanitizer.Redact(text));
    }
    [Theory]
    [InlineData("MRN: 116732263000001", "")]
    [InlineData("FIN: 219435629", "")]
    [InlineData("DOD ID (EDIPI): 1207832630", "")]
    [InlineData("Veterans ID (ICN): 1022399772V106425", "")]
    public void Redact_NeutralizesAdditionalPatientIdentifiers(string original, string expected)
    {
        var result = VeteransReviewerPackagePrivacySanitizer.Redact(original);
        Assert.Equal(expected, result);
        Assert.Equal(result, VeteransReviewerPackagePrivacySanitizer.Redact(result));
    }

    [Fact]
    public void Redact_MasksIcnWhenFixedLayoutPlacesValueBeforeItsLabel()
    {
        const string text = "DOD ID (EDIPI): 1207832630 1022399772V106425Veterans ID (ICN):";
        var result = VeteransReviewerPackagePrivacySanitizer.Redact(text);
        Assert.DoesNotContain("1207832630", result);
        Assert.DoesNotContain("1022399772V106425", result);
        Assert.Equal("", result.Trim());
        Assert.DoesNotContain("6425", result);
    }

    [Theory]
    [InlineData("MRN: 123-45-5668", "Patient identifier: 5668")]
    [InlineData("SSN: 123-45-5668", "Patient identifier: 5668")]
    public void StandaloneIdentifierLine_UsesNeutralReviewerPresentation(string original, string expected)
    {
        Assert.True(VeteransReviewerPackagePrivacySanitizer.TryNeutralizeStandalonePatientIdentifier(
            original, out var neutral));
        Assert.Equal(expected, neutral);
    }

    [Fact]
    public void NarrativeIdentifierReference_IsNotCollapsedIntoStandalonePatientIdentifier()
    {
        Assert.False(VeteransReviewerPackagePrivacySanitizer.TryNeutralizeStandalonePatientIdentifier(
            "Narrative SSN: 123-45-5668; finding retained.", out _));
    }

    [Theory]
    [InlineData("fingernails")]
    [InlineData("Examine fingernails and fingertips.")]
    [InlineData("FINancial history reviewed.")]
    public void Redact_RequiresRealIdentifierField(string text) =>
        Assert.Equal(text, VeteransReviewerPackagePrivacySanitizer.Redact(text));

    [Fact]
    public void Redact_SuppressesReorderedPowerFormIdentifiersWithoutFragmentingLabels()
    {
        const string text = "1207832630 Veterans ID (ICN): 1022399772V106425DOD ID (EDIPI):";
        Assert.Equal("", VeteransReviewerPackagePrivacySanitizer.Redact(text));
    }
}
