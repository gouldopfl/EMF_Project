using EMF.Discovery.Services;

namespace EMF.Tests;

public sealed class HtmlContentInspectorTests
{
    [Fact]
    public void Inspect_ReportsHtmlStructureAndTitle()
    {
        var inspector = new HtmlContentInspector();
        var metadata = new Dictionary<string, object>();
        var findings = new List<string>();

        inspector.Inspect(
            """
            <html>
              <head><title>Veteran Evidence</title></head>
              <body><p>Evidence content.</p></body>
            </html>
            """u8.ToArray(),
            metadata,
            findings);

        Assert.True(
            (bool)metadata["htmlHasHtmlElement"]);

        Assert.True(
            (bool)metadata["htmlHasBodyElement"]);

        Assert.Equal(
            "Veteran Evidence",
            metadata["htmlTitle"]);

        Assert.Contains(
            findings,
            finding => finding.Contains(
                "HTML document structure",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Inspect_RecognizesBodyWithoutHtmlElement()
    {
        var inspector = new HtmlContentInspector();
        var metadata = new Dictionary<string, object>();
        var findings = new List<string>();

        inspector.Inspect(
            "<body><p>Evidence</p></body>"u8.ToArray(),
            metadata,
            findings);

        Assert.False(
            (bool)metadata["htmlHasHtmlElement"]);

        Assert.True(
            (bool)metadata["htmlHasBodyElement"]);
    }

    [Fact]
    public void Inspect_ReportsUnrecognizedText()
    {
        var inspector = new HtmlContentInspector();
        var metadata = new Dictionary<string, object>();
        var findings = new List<string>();

        inspector.Inspect(
            "This is ordinary text."u8.ToArray(),
            metadata,
            findings);

        Assert.False(
            (bool)metadata["htmlHasHtmlElement"]);

        Assert.False(
            (bool)metadata["htmlHasBodyElement"]);

        Assert.Contains(
            findings,
            finding => finding.Contains(
                "does not contain recognizable HTML",
                StringComparison.Ordinal));
    }
}
