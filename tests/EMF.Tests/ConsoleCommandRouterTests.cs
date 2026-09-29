using EMF.ConsoleApplication;

namespace EMF.Tests;

public sealed class ConsoleCommandRouterTests
{
    [Fact]
    public async Task IntelligenceCommand_RequiresAnalyzeArguments()
    {
        var exitCode =
            await ConsoleCommandRouter.RunAsync(
                ["intelligence"]);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task HelpCommand_Succeeds()
    {
        var exitCode =
            await ConsoleCommandRouter.RunAsync(
                ["help"]);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task SummarizeCommand_RequiresTextFile()
    {
        var exitCode =
            await ConsoleCommandRouter.RunAsync(
                ["intelligence", "summarize"]);

        Assert.Equal(2, exitCode);
    }


    [Fact]
    public async Task UnknownCommand_ReturnsUsageError()
    {
        var exitCode =
            await ConsoleCommandRouter.RunAsync(
                ["inteligence"]);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task LegacyInventoryWithTooManyArguments_ReturnsUsageError()
    {
        var exitCode =
            await ConsoleCommandRouter.RunAsync(
                ["./dataset", "workflow-id", "unexpected"]);

        Assert.Equal(2, exitCode);
    }


    [Fact]
    public async Task VeteransCommand_RequiresEvidenceDevelopmentArguments()
    {
        var exitCode =
            await ConsoleCommandRouter.RunAsync(
                ["veterans"]);

        Assert.Equal(2, exitCode);
    }


    [Fact]
    public async Task BuildIdentityCommand_Succeeds()
    {
        var exitCode =
            await ConsoleCommandRouter.RunAsync(
                ["build", "identity"]);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task BuildManifestAndVerifyCommands_Succeed()
    {
        var path =
            Path.Combine(
                Path.GetTempPath(),
                $"emf-build-manifest-{Guid.NewGuid():N}.json");

        try
        {
            Assert.Equal(
                0,
                await ConsoleCommandRouter.RunAsync(
                    ["build", "manifest", path]));

            Assert.True(File.Exists(path));

            Assert.Equal(
                0,
                await ConsoleCommandRouter.RunAsync(
                    ["build", "verify", path]));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task BuildVerifyCommand_MissingManifestReturnsFailure()
    {
        var path =
            Path.Combine(
                Path.GetTempPath(),
                $"emf-missing-build-{Guid.NewGuid():N}.json");

        var exitCode =
            await ConsoleCommandRouter.RunAsync(
                ["build", "verify", path]);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task BuildVerifyCommand_MalformedManifestReturnsFailure()
    {
        var path =
            Path.Combine(
                Path.GetTempPath(),
                $"emf-malformed-build-{Guid.NewGuid():N}.json");

        try
        {
            await File.WriteAllTextAsync(
                path,
                "{ this is not valid json");

            var exitCode =
                await ConsoleCommandRouter.RunAsync(
                    ["build", "verify", path]);

            Assert.Equal(1, exitCode);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task BuildManifestCommand_WriteFailureReturnsFailure()
    {
        var exitCode =
            await ConsoleCommandRouter.RunAsync(
                ["build", "manifest", Path.GetTempPath()]);

        Assert.Equal(1, exitCode);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"Artifacts\":null}")]
    [InlineData("{\"Artifacts\":[null]}")]
    [InlineData("{\"Artifacts\":[null,null]}")]
    [InlineData("{\"Artifacts\":[{},null]}")]
    public async Task BuildVerifyCommand_InvalidStructureReturnsFailure(string json)
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, json);
            Assert.Equal(1, await ConsoleCommandRouter.RunAsync(["build", "verify", path]));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task SecurityCommand_RequiresAuditVerifyArguments()
    {
        var exitCode =
            await ConsoleCommandRouter.RunAsync(
                ["security"]);

        Assert.Equal(2, exitCode);
    }
}
