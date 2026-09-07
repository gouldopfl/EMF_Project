using EMF.ArchitectureAuditor;
using Xunit;

namespace EMF.ArchitectureAuditor.Tests;

public sealed class VeteransClaimsDependencyRuleTests
{
    private static readonly string[] Projects =
    [
        "EMF.Extensions.VeteransClaims",
        "EMF.Extensions.VeteransClaims.Orchestration",
        "EMF.Extensions.VeteransClaims.Persistence",
        "EMF.Extensions.VeteransClaims.Persistence.Sqlite"
    ];

    [Fact]
    public void Analyze_DetectsUnapprovedVeteransClaimsDependency()
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
                "EMF.Extensions.VeteransClaims",
                "../EMF.Console/EMF.Console.csproj");

            var finding =
                Assert.Single(
                    new VeteransClaimsDependencyRule()
                        .Analyze(root));

            Assert.Equal("EMF-ARCH-005", finding.RuleId);
            Assert.Equal(AuditSeverity.High, finding.Severity);
            Assert.Contains("EMF.Console", finding.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Analyze_ActualVeteransClaimsProjectsPass()
    {
        Assert.Empty(
            new VeteransClaimsDependencyRule()
                .Analyze(FindRepositoryRoot()));
    }

    private static void CreateProject(
        string root,
        string project,
        string? reference = null)
    {
        var directory = Path.Combine(root, "src", project);
        Directory.CreateDirectory(directory);

        var item =
            reference is null
                ? ""
                : $"""<ItemGroup><ProjectReference Include="{reference}" /></ItemGroup>""";

        File.WriteAllText(
            Path.Combine(directory, $"{project}.csproj"),
            $"""<Project Sdk="Microsoft.NET.Sdk">{item}</Project>""");
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
