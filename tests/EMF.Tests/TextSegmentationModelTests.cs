using EMF.Intelligence.Capabilities;

namespace EMF.Tests;

public sealed class TextSegmentationModelTests
{
    [Fact]
    public void Request_CapturesSegmentationSettings()
    {
        var request =
            new TextSegmentationRequest(
                "Source document",
                8,
                2);

        Assert.Equal(
            "Source document",
            request.Text);
        Assert.Equal(
            8,
            request.MaximumSegmentCharacters);
        Assert.Equal(
            2,
            request.OverlapCharacters);
    }

    [Fact]
    public void Request_RejectsInvalidOverlap()
    {
        Assert.Throws<
            ArgumentOutOfRangeException>(
            () =>
                new TextSegmentationRequest(
                    "Source document",
                    8,
                    8));
    }

    [Fact]
    public void Segment_PreservesOffsetsAndWhitespace()
    {
        var segment =
            new TextSegment(
                2,
                10,
                " \n ");

        Assert.Equal(2, segment.Index);
        Assert.Equal(10, segment.StartOffset);
        Assert.Equal(13, segment.EndOffset);
        Assert.Equal(" \n ", segment.Text);
    }
}
