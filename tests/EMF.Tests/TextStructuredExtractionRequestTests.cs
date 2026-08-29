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
                """{"outcome":"string"}""");

        Assert.Equal("Decision text.", request.Text);
        Assert.Equal(
            "Extract the decision.",
            request.Instruction);
        Assert.Equal(
            """{"outcome":"string"}""",
            request.JsonSchema);
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
}
