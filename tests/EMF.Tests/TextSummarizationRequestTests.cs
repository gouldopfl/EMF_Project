using EMF.Intelligence.Capabilities;

namespace EMF.Tests;

public sealed class TextSummarizationRequestTests
{
    [Fact]
    public void Constructor_CapturesTextAndLimit()
    {
        var request =
            new TextSummarizationRequest(
                "A longer source document.",
                12);

        Assert.Equal(
            "A longer source document.",
            request.Text);

        Assert.Equal(
            12,
            request.MaximumCharacters);
    }

    [Fact]
    public void Constructor_RejectsInvalidValues()
    {
        Assert.ThrowsAny<ArgumentException>(
            () =>
                new TextSummarizationRequest(
                    " ",
                    12));

        Assert.Throws<
            ArgumentOutOfRangeException>(
            () =>
                new TextSummarizationRequest(
                    "Source",
                    0));
    }
}
