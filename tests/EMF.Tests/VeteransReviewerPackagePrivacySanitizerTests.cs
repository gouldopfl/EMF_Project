using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.Tests;

public sealed class VeteransReviewerPackagePrivacySanitizerTests
{
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
            "SSN: *****6789",
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
}
