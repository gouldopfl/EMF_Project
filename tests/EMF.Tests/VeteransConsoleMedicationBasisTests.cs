using EMF.ConsoleApplication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

namespace EMF.Tests;

public sealed partial class VeteransConsoleCommandTests
{
    [Fact]
    public async Task EvidenceMedicationBasis_PersistsAssociation()
    {
        var path = Path.GetTempFileName();

        try
        {
            await SeedMedicationBasisDatabaseAsync(path);
            using var output = new StringWriter();

            var exitCode =
                await VeteransConsoleCommand.RunEvidenceMedicationBasisAsync(
                    path,
                    new ServiceConnectionBasisId("basis-med-console"),
                    "Trazodone HCl",
                    output);

            Assert.Equal(0, exitCode);

            var repository =
                new SqliteServiceConnectionRepository(path);

            var names =
                await repository.GetPrescribedMedicationNamesAsync(
                    new ServiceConnectionBasisId("basis-med-console"));

            Assert.Equal("Trazodone HCl", Assert.Single(names));
            Assert.Contains("Status     : Persisted", output.ToString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task EvidenceMedicationBasis_IsIdempotent()
    {
        var path = Path.GetTempFileName();

        try
        {
            await SeedMedicationBasisDatabaseAsync(path);

            using var first = new StringWriter();
            using var second = new StringWriter();

            var basisId =
                new ServiceConnectionBasisId("basis-med-console");

            Assert.Equal(
                0,
                await VeteransConsoleCommand.RunEvidenceMedicationBasisAsync(
                    path, basisId, "Trazodone HCl", first));

            Assert.Equal(
                0,
                await VeteransConsoleCommand.RunEvidenceMedicationBasisAsync(
                    path, basisId, "trazodone hcl", second));

            var repository =
                new SqliteServiceConnectionRepository(path);

            Assert.Single(
                await repository.GetPrescribedMedicationNamesAsync(
                    basisId));

            Assert.Contains("Status     : Existing", second.ToString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task EvidenceMedicationBasis_RejectsMissingBasis()
    {
        var path = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(path)
                .InitializeAsync();

            using var output = new StringWriter();

            var exitCode =
                await VeteransConsoleCommand.RunEvidenceMedicationBasisAsync(
                    path,
                    new ServiceConnectionBasisId("missing-basis"),
                    "Trazodone HCl",
                    output);

            Assert.Equal(1, exitCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task SeedMedicationBasisDatabaseAsync(
        string path)
    {
        await new VeteransClaimsSqliteSchema(path)
            .InitializeAsync();

        var veteran =
            new Veteran
            {
                Id = new VeteranId("veteran-med-console")
            };

        await new SqliteVeteranRepository(path)
            .AddVeteranAsync(veteran);

        var claim =
            new Claim
            {
                Id = new ClaimId("claim-med-console"),
                VeteranId = veteran.Id
            };

        await new SqliteClaimRepository(path)
            .AddClaimAsync(claim);

        var issue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-med-console"),
                ClaimId = claim.Id,
                ClaimIssueType = ClaimIssueTypes.ServiceConnection
            };

        await new SqliteClaimIssueRepository(path)
            .AddClaimIssueAsync(issue);

        var repository =
            new SqliteServiceConnectionRepository(path);

        var theory =
            new ServiceConnectionTheory
            {
                Id = new ServiceConnectionTheoryId("theory-med-console"),
                ClaimIssueId = issue.Id,
                TheoryType = ServiceConnectionTheoryTypes.Secondary
            };

        await repository.AddServiceConnectionTheoryAsync(theory);

        await repository.AddServiceConnectionBasisAsync(
            new ServiceConnectionBasis
            {
                Id = new ServiceConnectionBasisId("basis-med-console"),
                ClaimIssueId = issue.Id,
                ServiceConnectionTheoryId = theory.Id
            });
    }
}
