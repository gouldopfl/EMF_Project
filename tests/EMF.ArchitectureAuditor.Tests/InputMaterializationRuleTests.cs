using EMF.ArchitectureAuditor;
using Xunit;

namespace EMF.ArchitectureAuditor.Tests;

public sealed class InputMaterializationRuleTests
{
    [Fact]
    public void Analyze_DetectsUnguardedByteInputMaterialization()
    {
        const string code = """
            namespace Example;

            public sealed class Parser
            {
                public void Parse(ReadOnlyMemory<byte> content)
                {
                    var bytes = content.ToArray();
                }
            }
            """;

        var parsed = Parse(code);
        var rule = new InputMaterializationRule();

        var finding = Assert.Single(rule.Analyze(parsed));

        Assert.Equal("EMF-RESOURCE-002", finding.RuleId);
        Assert.Equal("2", finding.RuleVersion);
        Assert.Equal(AuditSeverity.Medium, finding.Severity);
        Assert.Equal(AuditConfidence.Medium, finding.Confidence);
        Assert.Equal(AuditAnalysisMode.Syntax, finding.AnalysisMode);
        Assert.Equal(AuditSourceArea.Production, finding.SourceArea);
    }

    [Fact]
    public void Analyze_AllowsPriorRejectingLengthGuard()
    {
        const string code = """
            namespace Example;

            public sealed class Parser
            {
                public void Parse(
                    ReadOnlyMemory<byte> content,
                    long maxInputBytes)
                {
                    if (content.Length > maxInputBytes)
                    {
                        throw new InvalidDataException("Too large.");
                    }

                    var bytes = content.ToArray();
                }
            }
            """;

        var parsed = Parse(code);
        var rule = new InputMaterializationRule();

        Assert.Empty(rule.Analyze(parsed));
    }

    [Fact]
    public void Analyze_DoesNotApplyProductionRuleToTestSource()
    {
        const string code = """
            public sealed class FakeStore
            {
                public void Write(ReadOnlyMemory<byte> content)
                {
                    var bytes = content.ToArray();
                }
            }
            """;

        var parsed =
            Parse(
                code,
                "tests/Example/FakeStoreTests.cs");

        var rule = new InputMaterializationRule();

        Assert.Empty(rule.Analyze(parsed));
    }

    private static ParsedSourceFile Parse(
        string code,
        string relativePath = "src/EMF.Core/TestSource.cs")
    {
        var path = Path.GetTempFileName();

        try
        {
            File.WriteAllText(path, code);

            var info = new FileInfo(path);
            var source = new SourceFile(
                relativePath,
                path,
                info.Length);

            return new SourceParser().Parse(source);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
