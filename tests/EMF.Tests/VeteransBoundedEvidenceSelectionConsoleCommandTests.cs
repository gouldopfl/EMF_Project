using EMF.ConsoleApplication;

namespace EMF.Tests;

public sealed class VeteransBoundedEvidenceSelectionConsoleCommandTests
{
    [Fact]
    public async Task BoundedListCommand_RejectsMissingDatabase()
    {
        var exitCode =
            await VeteransConsoleCommand.RunAsync(
                [
                    "evidence",
                    "bounded",
                    "list",
                    "/tmp/emf-missing-bounded-list.db",
                    "issue-osa",
                    "basis-osa-secondary",
                    "requirement-3.310-a-secondary-causation",
                    "blue-button-001"
                ]);

        Assert.Equal(2, exitCode);
    }
}
