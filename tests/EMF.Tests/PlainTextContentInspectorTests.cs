using EMF.Discovery.Services;

namespace EMF.Tests;

public sealed class PlainTextContentInspectorTests
{
    [Fact]
    public void Inspect_RecognizesReadableText()
    {
        var inspector = new PlainTextContentInspector();
        var metadata = new Dictionary<string, object>();
        var findings = new List<string>();

        inspector.Inspect(
            "Veteran evidence document.\nSecond line."u8.ToArray(),
            metadata,
            findings);

        Assert.True(
            (double)metadata["textPrintableRatio"] >= 0.95);

        Assert.Contains(
            findings,
            finding => finding.Contains(
                "readable plain text",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Inspect_ReportsControlCharacters()
    {
        var inspector = new PlainTextContentInspector();
        var metadata = new Dictionary<string, object>();
        var findings = new List<string>();

        inspector.Inspect(
            new byte[]
            {
                0x41, 0x42, 0x43, 0x00, 0x01, 0x02
            },
            metadata,
            findings);

        Assert.Contains(
            findings,
            finding => finding.Contains(
                "control characters",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Inspect_ReportsInvalidUtf8()
    {
        var inspector = new PlainTextContentInspector();
        var metadata = new Dictionary<string, object>();
        var findings = new List<string>();

        inspector.Inspect(
            new byte[] { 0xFF, 0xFE, 0xFD },
            metadata,
            findings);

        Assert.Contains(
            findings,
            finding => finding.Contains(
                "not valid UTF-8",
                StringComparison.Ordinal));
    }
}
