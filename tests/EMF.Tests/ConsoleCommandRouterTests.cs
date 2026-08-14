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
}
