using EMF.Intelligence.Capabilities;

namespace EMF.Tests;

public sealed class TextStructuredExtractionRequestTests
{
    [Fact]
    public void Constructor_PreservesConfiguration()
    {
        var request =
            new TextStructuredExtractionRequest(
                "Decision text.",
                "Extract the decision.",
                """{"outcome":"string"}""",
                512);

        Assert.Equal("Decision text.", request.Text);
        Assert.Equal(
            "Extract the decision.",
            request.Instruction);
        Assert.Equal(
            """{"outcome":"string"}""",
            request.JsonSchema);
        Assert.Equal(512, request.MaximumOutputTokenCount);
    }

    [Theory]
    [InlineData("", "instruction", "{}")]
    [InlineData("text", "", "{}")]
    [InlineData("text", "instruction", "")]
    public void Constructor_RejectsMissingConfiguration(
        string text,
        string instruction,
        string jsonSchema)
    {
        Assert.Throws<ArgumentException>(
            () => new TextStructuredExtractionRequest(
                text,
                instruction,
                jsonSchema));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsInvalidMaximumOutputTokenCount(
        int maximumOutputTokenCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TextStructuredExtractionRequest(
                "text",
                "instruction",
                "{}",
                maximumOutputTokenCount));
    }

}
