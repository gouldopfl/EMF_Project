using EMF.ArchitectureAuditor;
using Xunit;

namespace EMF.ArchitectureAuditor.Tests;

public sealed class DirectPackageOwnershipRuleTests
{
    [Fact]
    public void Analyze_DetectsTransitivePackageUsage()
    {
        var root = CreateRepository(
            """
            <Project Sdk="Microsoft.NET.Sdk">
            </Project>
            """);

        try
        {
            var parsed = Parse(
                """
                using Microsoft.Data.Sqlite;

                namespace EMF.Core;

                public sealed class Store
                {
                    public SqliteConnection Create() => new();
                }
                """,
                "src/EMF.Core/Store.cs");

            var finding =
                Assert.Single(
                    new DirectPackageOwnershipRule(root)
                        .Analyze(parsed));

            Assert.Equal("EMF-ARCH-008", finding.RuleId);
            Assert.Equal("1", finding.RuleVersion);
            Assert.Equal(AuditSeverity.High, finding.Severity);
            Assert.Equal(AuditConfidence.High, finding.Confidence);
            Assert.Equal(AuditAnalysisMode.Syntax, finding.AnalysisMode);
            Assert.Contains("Microsoft.Data.Sqlite", finding.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Analyze_AllowsDirectPackageOwnership()
    {
        var root = CreateRepository(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Microsoft.Data.Sqlite"
                                  Version="10.0.11" />
              </ItemGroup>
            </Project>
            """);

        try
        {
            var parsed = Parse(
                """
                using Microsoft.Data.Sqlite;

                namespace EMF.Core;

                public sealed class Store
                {
                    public SqliteConnection Create() => new();
                }
                """,
                "src/EMF.Core/Store.cs");

            Assert.Empty(
                new DirectPackageOwnershipRule(root)
                    .Analyze(parsed));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Analyze_ActualProductionSourcesPass()
    {
        var root = FindRepositoryRoot();
        var rule = new DirectPackageOwnershipRule(root);
        var parser = new SourceParser();

        var findings =
            new SourceInventory()
                .Discover(root)
                .Select(source => parser.Parse(source))
                .SelectMany(source => rule.Analyze(source))
                .ToArray();

        Assert.Empty(findings);
    }

    private static string CreateRepository(string projectContent)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"emf-auditor-{Guid.NewGuid():N}");

        var directory =
            Path.Combine(root, "src", "EMF.Core");

        Directory.CreateDirectory(directory);

        File.WriteAllText(
            Path.Combine(directory, "EMF.Core.csproj"),
            projectContent);

        return root;
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
