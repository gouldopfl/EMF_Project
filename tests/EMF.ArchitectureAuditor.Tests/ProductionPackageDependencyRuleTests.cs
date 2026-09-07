using EMF.ArchitectureAuditor;
using Xunit;

namespace EMF.ArchitectureAuditor.Tests;

public sealed class ProductionPackageDependencyRuleTests
{
    [Fact]
    public void Analyze_DetectsUnapprovedProductionPackage()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"emf-auditor-{Guid.NewGuid():N}");

        try
        {
            var directory =
                Path.Combine(root, "src", "EMF.Core");

            Directory.CreateDirectory(directory);

            File.WriteAllText(
                Path.Combine(directory, "EMF.Core.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="Azure.Identity"
                                      Version="1.21.0" />
                  </ItemGroup>
                </Project>
                """);

            var finding =
                Assert.Single(
                    new ProductionPackageDependencyRule()
                        .Analyze(root));

            Assert.Equal("EMF-ARCH-007", finding.RuleId);
            Assert.Equal(AuditSeverity.High, finding.Severity);
            Assert.Equal(AuditConfidence.High, finding.Confidence);
            Assert.Contains("EMF.Core", finding.Message);
            Assert.Contains("Azure.Identity", finding.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Analyze_ActualProductionPackagesPass()
    {
        Assert.Empty(
            new ProductionPackageDependencyRule()
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
