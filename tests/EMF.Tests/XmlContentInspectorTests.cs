using EMF.Discovery.Services;

namespace EMF.Tests;

public sealed class XmlContentInspectorTests
{
    [Fact]
    public void Inspect_ReportsRootAndElementCount()
    {
        var inspector = new XmlContentInspector();
        var metadata = new Dictionary<string, object>();
        var findings = new List<string>();

        inspector.Inspect(
            "<evidence><id>1</id><status>Active</status></evidence>"u8.ToArray(),
            metadata,
            findings);

        Assert.Equal(
            "evidence",
            metadata["xmlRootElement"]);

        Assert.Equal(
            3,
            metadata["xmlElementCount"]);

        Assert.Contains(
            findings,
            finding => finding.Contains(
                "Valid XML",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Inspect_HandlesNestedElements()
    {
        var inspector = new XmlContentInspector();
        var metadata = new Dictionary<string, object>();
        var findings = new List<string>();

        inspector.Inspect(
            "<root><veteran><claim><id>1</id></claim></veteran></root>"u8.ToArray(),
            metadata,
            findings);

        Assert.Equal(
            "root",
            metadata["xmlRootElement"]);

        Assert.Equal(
            4,
            metadata["xmlElementCount"]);
    }

    [Fact]
    public void Inspect_ReportsMalformedXml()
    {
        var inspector = new XmlContentInspector();
        var metadata = new Dictionary<string, object>();
        var findings = new List<string>();

        inspector.Inspect(
            "<root><broken>"u8.ToArray(),
            metadata,
            findings);

        Assert.Contains(
            findings,
            finding => finding.Contains(
                "not valid XML",
                StringComparison.Ordinal));
    }
}
