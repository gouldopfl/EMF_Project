using EMF.Discovery.Services;

namespace EMF.Tests;

public sealed class EmlContentInspectorTests
{
    [Fact]
    public void Inspect_RecognizesEmailHeaders()
    {
        var inspector = new EmlContentInspector();
        var metadata = new Dictionary<string, object>();
        var findings = new List<string>();

        inspector.Inspect(
            """
            From: sender@example.com
            To: recipient@example.com
            Subject: Evidence
            Date: Sat, 22 Aug 2026 18:00:00 -0400
            MIME-Version: 1.0

            Evidence attached.
            """u8.ToArray(),
            metadata,
            findings);

        Assert.True((bool)metadata["emailHasFrom"]);
        Assert.True((bool)metadata["emailHasTo"]);
        Assert.True((bool)metadata["emailHasSubject"]);
        Assert.True((bool)metadata["emailHasDate"]);
        Assert.True((bool)metadata["emailHasMimeVersion"]);

        Assert.Contains(
            findings,
            finding => finding.Contains(
                "EML message headers",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Inspect_RecognizesMinimalEmail()
    {
        var inspector = new EmlContentInspector();
        var metadata = new Dictionary<string, object>();
        var findings = new List<string>();

        inspector.Inspect(
            "From: a@example.com\nTo: b@example.com\n\nHello"u8.ToArray(),
            metadata,
            findings);

        Assert.True((bool)metadata["emailHasFrom"]);
        Assert.True((bool)metadata["emailHasTo"]);
    }

    [Fact]
    public void Inspect_RejectsInsufficientHeaders()
    {
        var inspector = new EmlContentInspector();
        var metadata = new Dictionary<string, object>();
        var findings = new List<string>();

        inspector.Inspect(
            "This is ordinary text."u8.ToArray(),
            metadata,
            findings);

        Assert.False((bool)metadata["emailHasFrom"]);
        Assert.False((bool)metadata["emailHasTo"]);

        Assert.Contains(
            findings,
            finding => finding.Contains(
                "Content does not contain sufficient EML headers.",
                StringComparison.Ordinal));
    }
}
