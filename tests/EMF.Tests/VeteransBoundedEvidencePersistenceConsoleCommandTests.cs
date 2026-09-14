using EMF.ConsoleApplication;

namespace EMF.Tests;

public sealed class VeteransBoundedEvidencePersistenceConsoleCommandTests
{
    [Fact]
    public async Task PersistCommand_RejectsMissingDatabase()
    {
        var exitCode =
            await VeteransConsoleCommand.RunAsync(
                [
                    "evidence",
                    "recognition",
                    "persist",
                    "/tmp/emf-missing-bounded-evidence.db",
                    "/tmp/emf-missing-recognition-snapshot.json",
                    "issue-osa",
                    "blue-button-001"
                ]);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task PersistCommand_RejectsMissingSnapshot()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var exitCode =
                await VeteransConsoleCommand.RunAsync(
                    [
                        "evidence",
                        "recognition",
                        "persist",
                        databasePath,
                        "/tmp/emf-missing-recognition-snapshot.json",
                        "issue-osa",
                        "blue-button-001"
                    ]);

            Assert.Equal(2, exitCode);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}
