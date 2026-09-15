using EMF.ConsoleApplication;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed partial class VeteransConsoleCommandTests
{
    [Fact]
    public async Task EvidenceMedicationLedgerImport_RequiresContentStore()
    {
        using var output = new StringWriter();

        var exitCode =
            await VeteransConsoleCommand
                .RunEvidenceMedicationLedgerImportAsync(
                    "/tmp/emf-medication-ledger-import.db",
                    new VeteranId("veteran-med-ledger-import"),
                    new ArtifactId("blue-button-2026-09-09"),
                    null,
                    output);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task EvidenceMedicationLedgerImport_RejectsMissingDatabase()
    {
        var path =
            Path.Combine(
                Path.GetTempPath(),
                $"emf-missing-medication-ledger-{Guid.NewGuid():N}.db");

        var exitCode =
            await VeteransConsoleCommand.RunAsync(
                [
                    "evidence",
                    "medication",
                    "ledger",
                    "import",
                    path,
                    "veteran-med-ledger-import",
                    "blue-button-2026-09-09"
                ]);

        Assert.Equal(2, exitCode);
        Assert.False(File.Exists(path));
    }
}
