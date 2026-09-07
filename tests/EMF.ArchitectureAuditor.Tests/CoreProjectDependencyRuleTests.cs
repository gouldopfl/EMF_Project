using EMF.ArchitectureAuditor;
using Xunit;

namespace EMF.ArchitectureAuditor.Tests;

public sealed class CoreProjectDependencyRuleTests
{
    [Fact]
    public void Analyze_DetectsCoreProjectReference()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"emf-auditor-{Guid.NewGuid():N}");

        var coreDirectory =
            Path.Combine(root, "src", "EMF.Core");

        Directory.CreateDirectory(coreDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(coreDirectory, "EMF.Core.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="../EMF.Console/EMF.Console.csproj" />
                  </ItemGroup>
                </Project>
                """);

            var rule = new CoreProjectDependencyRule();

            var finding =
                Assert.Single(rule.Analyze(root));

            Assert.Equal("EMF-ARCH-001", finding.RuleId);
            Assert.Equal(AuditSeverity.High, finding.Severity);
            Assert.Equal(AuditConfidence.High, finding.Confidence);
            Assert.Contains("EMF.Console", finding.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Analyze_ActualCoreProjectHasNoProjectReferences()
    {
        var root = FindRepositoryRoot();
        var rule = new CoreProjectDependencyRule();

        var findings = rule.Analyze(root).ToArray();

        Assert.Empty(findings);
    }

    private static string FindRepositoryRoot()
    {
        var directory =
            new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(
                    Path.Combine(directory.FullName, "EMF.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "EMF repository root was not found.");
    }
}
