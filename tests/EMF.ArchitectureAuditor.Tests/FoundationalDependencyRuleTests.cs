using EMF.ArchitectureAuditor;
using Xunit;

namespace EMF.ArchitectureAuditor.Tests;

public sealed class FoundationalDependencyRuleTests
{
    private static readonly string[] Projects =
    [
        "EMF.Common",
        "EMF.Discovery",
        "EMF.Inventory",
        "EMF.Integrity",
        "EMF.Persistence",
        "EMF.Security",
        "EMF.Intelligence",
        "EMF.Orchestration"
    ];

    [Fact]
    public void Analyze_DetectsUnapprovedFoundationalDependency()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"emf-auditor-{Guid.NewGuid():N}");

        try
        {
            foreach (var project in Projects)
                CreateProject(root, project);

            CreateProject(
                root,
                "EMF.Discovery",
                "../EMF.Console/EMF.Console.csproj");

            var finding =
                Assert.Single(
                    new FoundationalDependencyRule()
                        .Analyze(root));

            Assert.Equal("EMF-ARCH-002", finding.RuleId);
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
    public void Analyze_ActualFoundationalProjectsPass()
    {
        var findings =
            new FoundationalDependencyRule()
                .Analyze(FindRepositoryRoot())
                .ToArray();

        Assert.Empty(findings);
    }

    private static void CreateProject(
        string root,
        string project,
        string? reference = null)
    {
        var directory =
            Path.Combine(root, "src", project);

        Directory.CreateDirectory(directory);

        var itemGroup =
            reference is null
                ? string.Empty
                : $"""
                  <ItemGroup>
                    <ProjectReference Include="{reference}" />
                  </ItemGroup>
                  """;

        File.WriteAllText(
            Path.Combine(directory, $"{project}.csproj"),
            $"""
             <Project Sdk="Microsoft.NET.Sdk">
             {itemGroup}
             </Project>
             """);
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
