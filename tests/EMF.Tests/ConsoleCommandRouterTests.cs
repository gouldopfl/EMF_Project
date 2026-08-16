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
    public async Task SecurityCommand_RequiresAuditVerifyArguments()
    {
        var exitCode =
            await ConsoleCommandRouter.RunAsync(
                ["security"]);

        Assert.Equal(2, exitCode);
    }
}
