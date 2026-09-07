using EMF.ArchitectureAuditor;
using Xunit;

namespace EMF.ArchitectureAuditor.Tests;

public sealed class ConsoleCompositionDependencyRuleTests
{
    [Fact]
    public void Analyze_DetectsUnapprovedConsoleDependency()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"emf-auditor-{Guid.NewGuid():N}");

        try
        {
            var directory =
                Path.Combine(root, "src", "EMF.Console");

            Directory.CreateDirectory(directory);

            File.WriteAllText(
                Path.Combine(directory, "EMF.Console.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="..\EMF.Laboratory\EMF.Laboratory.csproj" />
                  </ItemGroup>
                </Project>
                """);

            var finding =
                Assert.Single(
                    new ConsoleCompositionDependencyRule()
                        .Analyze(root));

            Assert.Equal("EMF-ARCH-006", finding.RuleId);
            Assert.Equal(AuditSeverity.High, finding.Severity);
            Assert.Equal(AuditConfidence.High, finding.Confidence);
            Assert.Contains("EMF.Laboratory", finding.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Analyze_ActualConsoleCompositionPasses()
    {
        Assert.Empty(
            new ConsoleCompositionDependencyRule()
                .Analyze(FindRepositoryRoot()));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(
                    Path.Combine(directory.FullName, "EMF.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "EMF repository root was not found.");
    }
}
