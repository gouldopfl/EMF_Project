using EMF.ArchitectureAuditor;
using Xunit;

namespace EMF.ArchitectureAuditor.Tests;

public sealed class LaboratoryDependencyRuleTests
{
    [Fact]
    public void Analyze_DetectsUnapprovedLaboratoryDependency()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"emf-auditor-{Guid.NewGuid():N}");

        var directory =
            Path.Combine(root, "src", "EMF.Laboratory");

        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(
                Path.Combine(
                    directory,
                    "EMF.Laboratory.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="../EMF.Console/EMF.Console.csproj" />
                  </ItemGroup>
                </Project>
                """);

            var finding =
                Assert.Single(
                    new LaboratoryDependencyRule()
                        .Analyze(root));

            Assert.Equal("EMF-ARCH-003", finding.RuleId);
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
    public void Analyze_ActualLaboratoryProjectPasses()
    {
        var findings =
            new LaboratoryDependencyRule()
                .Analyze(FindRepositoryRoot())
                .ToArray();

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
