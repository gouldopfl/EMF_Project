using EMF.ArchitectureAuditor;
using Xunit;

namespace EMF.ArchitectureAuditor.Tests;

public sealed class WholeFileReadRuleTests
{
    [Fact]
    public void Analyze_DetectsProductionWholeFileRead()
    {
        const string code = """
            public sealed class Loader
            {
                public string Load(string path) =>
                    File.ReadAllText(path);
            }
            """;

        var parsed = Parse(
            code,
            "src/EMF.Core/Loader.cs");

        var finding =
            Assert.Single(
                new WholeFileReadRule().Analyze(parsed));

        Assert.Equal("EMF-RESOURCE-001", finding.RuleId);
        Assert.Equal(AuditSourceArea.Production, finding.SourceArea);
    }

    [Fact]
    public void Analyze_DoesNotApplyProductionRuleToTestSource()
    {
        const string code = """
            public sealed class Fixture
            {
                public byte[] Load(string path) =>
                    File.ReadAllBytes(path);
            }
            """;

        var parsed = Parse(
            code,
            "tests/Example/FixtureTests.cs");

        Assert.Empty(
            new WholeFileReadRule().Analyze(parsed));
    }

    private static ParsedSourceFile Parse(
        string code,
        string relativePath)
    {
        var path = Path.GetTempFileName();

        try
        {
            File.WriteAllText(path, code);

            var info = new FileInfo(path);

            return new SourceParser().Parse(
                new SourceFile(
                    relativePath,
                    path,
                    info.Length));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
