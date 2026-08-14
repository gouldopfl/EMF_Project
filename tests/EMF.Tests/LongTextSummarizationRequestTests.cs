using EMF.Intelligence.Capabilities;

namespace EMF.Tests;

public sealed class LongTextSummarizationRequestTests
{
    [Fact]
    public void Constructor_PreservesCoordinationSettings()
    {
        var request =
            new LongTextSummarizationRequest(
                "A long source document.",
                1_000,
                100,
                200);

        Assert.Equal(
            "A long source document.",
            request.Text);
        Assert.Equal(
            1_000,
            request.MaximumSegmentCharacters);
        Assert.Equal(
            100,
            request.OverlapCharacters);
        Assert.Equal(
            200,
            request.MaximumSummaryCharacters);
    }

    [Fact]
    public void Constructor_RejectsInvalidOverlap()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LongTextSummarizationRequest(
                "Source document.",
                100,
                100,
                50));
    }

    [Fact]
    public void Constructor_RejectsInvalidSummaryLimit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LongTextSummarizationRequest(
                "Source document.",
                100,
                10,
                0));
    }
}
