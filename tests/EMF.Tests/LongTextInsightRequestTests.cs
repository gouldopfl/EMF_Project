using EMF.Intelligence.Capabilities;

namespace EMF.Tests;

public sealed class LongTextInsightRequestTests
{
    [Fact]
    public void Constructor_PreservesAllSettings()
    {
        var request =
            new LongTextInsightRequest(
                "Alpha beta alpha gamma.",
                1_000,
                100,
                200,
                10,
                4,
                ["gamma"]);

        Assert.Equal(
            1_000,
            request.MaximumSegmentCharacters);
        Assert.Equal(
            100,
            request.OverlapCharacters);
        Assert.Equal(
            200,
            request.MaximumSummaryCharacters);
        Assert.Equal(10, request.MaximumKeywords);
        Assert.Equal(
            4,
            request.MinimumKeywordLength);
        Assert.Equal(
            ["gamma"],
            request.ExcludedTerms);
    }

    [Fact]
    public void Constructor_RejectsInvalidOverlap()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LongTextInsightRequest(
                "Source text.",
                100,
                100,
                20,
                5));
    }
}
