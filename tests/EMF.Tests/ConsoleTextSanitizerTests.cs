using EMF.ConsoleApplication;

namespace EMF.Tests;

public sealed class ConsoleTextSanitizerTests
{
    [Fact]
    public void Sanitize_PreservesPrintableTextAndWhitespace()
    {
        const string value =
            "Reviewer summary\r\n\tEvidence 😀";

        Assert.Equal(
            value,
            ConsoleTextSanitizer.Sanitize(value));
    }

    [Fact]
    public void Sanitize_RendersTerminalControlsVisibly()
    {
        const string value =
            "before\u001b[31mred\u001b[0m\u0000after";

        Assert.Equal(
            "before\\u001B[31mred\\u001B[0m\\u0000after",
            ConsoleTextSanitizer.Sanitize(value));
    }

    [Fact]
    public void Sanitize_RendersUnicodeFormatControlsVisibly()
    {
        Assert.Equal(
            "before\\u202Eafter",
            ConsoleTextSanitizer.Sanitize(
                "before\u202Eafter"));
    }

    [Fact]
    public void Sanitize_ConvertsNullToEmptyText()
    {
        Assert.Equal(
            string.Empty,
            ConsoleTextSanitizer.Sanitize(null));
    }
}
