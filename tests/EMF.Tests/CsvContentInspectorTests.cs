using EMF.Discovery.Services;

namespace EMF.Tests;

public sealed class CsvContentInspectorTests
{
    [Fact]
    public void Inspect_ReportsConsistentCsv()
    {
        var inspector = new CsvContentInspector();
        var metadata = new Dictionary<string, object>();
        var findings = new List<string>();

        inspector.Inspect(
            "id,name,status\n1,Veteran,Active\n2,Claim,Pending"u8.ToArray(),
            metadata,
            findings);

        Assert.Equal(3, metadata["csvColumnCount"]);
        Assert.Equal(3, metadata["csvSampleRowCount"]);
        Assert.True(
            (bool)metadata["csvConsistentColumnCount"]);

        Assert.Contains(
            findings,
            finding => finding.Contains(
                "consistent column counts",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Inspect_HandlesQuotedComma()
    {
        var inspector = new CsvContentInspector();
        var metadata = new Dictionary<string, object>();
        var findings = new List<string>();

        inspector.Inspect(
            "id,name\n1,\"Smith, Michael\""u8.ToArray(),
            metadata,
            findings);

        Assert.Equal(2, metadata["csvColumnCount"]);
        Assert.True(
            (bool)metadata["csvConsistentColumnCount"]);
    }

    [Fact]
    public void Inspect_ReportsInconsistentRows()
    {
        var inspector = new CsvContentInspector();
        var metadata = new Dictionary<string, object>();
        var findings = new List<string>();

        inspector.Inspect(
            "id,name\n1,Veteran\n2,Claim,Extra"u8.ToArray(),
            metadata,
            findings);

        Assert.False(
            (bool)metadata["csvConsistentColumnCount"]);

        Assert.Contains(
            findings,
            finding => finding.Contains(
                "inconsistent",
                StringComparison.OrdinalIgnoreCase));
    }
}
