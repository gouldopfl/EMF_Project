using EMF.Discovery.Services;

namespace EMF.Tests;

public sealed class JsonContentInspectorTests
{
    [Fact]
    public void Inspect_ReportsObjectStructure()
    {
        var inspector = new JsonContentInspector();
        var metadata = new Dictionary<string, object>();
        var findings = new List<string>();

        inspector.Inspect(
            """{"id":1,"name":"evidence"}"""u8.ToArray(),
            metadata,
            findings);

        Assert.Equal(
            "Object",
            metadata["jsonRootKind"]);

        Assert.Equal(
            2,
            metadata["jsonPropertyCount"]);

        Assert.Contains(
            findings,
            finding => finding.Contains(
                "Valid JSON",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Inspect_ReportsArrayStructure()
    {
        var inspector = new JsonContentInspector();
        var metadata = new Dictionary<string, object>();
        var findings = new List<string>();

        inspector.Inspect(
            """[{"id":1},{"id":2}]"""u8.ToArray(),
            metadata,
            findings);

        Assert.Equal(
            "Array",
            metadata["jsonRootKind"]);

        Assert.Equal(
            2,
            metadata["jsonElementCount"]);
    }

    [Fact]
    public void Inspect_ReportsMalformedJson()
    {
        var inspector = new JsonContentInspector();
        var metadata = new Dictionary<string, object>();
        var findings = new List<string>();

        inspector.Inspect(
            "{not valid json"u8.ToArray(),
            metadata,
            findings);

        Assert.Contains(
            findings,
            finding => finding.Contains(
                "not valid JSON",
                StringComparison.Ordinal));
    }
}
