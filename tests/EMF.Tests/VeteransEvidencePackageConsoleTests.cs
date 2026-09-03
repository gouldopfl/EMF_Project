using EMF.ConsoleApplication;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

namespace EMF.Tests;

public sealed partial class VeteransEvidencePackageConsoleTests
{
    [Fact]
    public async Task EvidencePackage_RejectsMissingDatabase()
    {
        var exitCode =
            await VeteransConsoleCommand.RunAsync(
                [
                    "evidence",
                    "package",
                    "/tmp/emf-missing-veterans-package.db",
                    "package-1"
                ]);

        Assert.Equal(2, exitCode);
    }
}

public sealed partial class VeteransEvidencePackageConsoleTests
{
    private static async Task<ClaimIssueId> SeedClaimIssueAsync(
        string databasePath)
    {
        await new VeteransClaimsSqliteSchema(
                databasePath)
            .InitializeAsync();

        var veteran = new Veteran
        {
            Id = new VeteranId("veteran-package-1")
        };

        await new SqliteVeteranRepository(databasePath)
            .AddVeteranAsync(veteran);

        var claim = new Claim
        {
            Id = new ClaimId("claim-package-1"),
            VeteranId = veteran.Id
        };

        await new SqliteClaimRepository(databasePath)
            .AddClaimAsync(claim);

        var issue = new ClaimIssue
        {
            Id = new ClaimIssueId("issue-package-1"),
            ClaimId = claim.Id,
            ClaimIssueType = ClaimIssueTypes.ServiceConnection
        };

        await new SqliteClaimIssueRepository(databasePath)
            .AddClaimIssueAsync(issue);

        return issue.Id;
    }
}

public sealed partial class VeteransEvidencePackageConsoleTests
{
    private static async Task<EvidencePackageId> SeedPackageAsync(
        string databasePath)
    {
        var issueId =
            await SeedClaimIssueAsync(databasePath);

        var packageId =
            new EvidencePackageId("package-1");

        await new SqliteEvidencePackageRepository(
                databasePath)
            .AddEvidencePackageAsync(
                new EvidencePackage
                {
                    Id = packageId,
                    ClaimIssueId = issueId,
                    Purpose = "Physician reviewer package",
                    ReviewerRole = "MedicalProfessional"
                },
                [
                    new EvidencePackageArtifact
                    {
                        EvidencePackageId = packageId,
                        ArtifactId = new ArtifactId("source-1"),
                        ContentRole =
                            EvidencePackageContentRoles.UnderlyingEvidence
                    },
                    new EvidencePackageArtifact
                    {
                        EvidencePackageId = packageId,
                        ArtifactId = new ArtifactId("summary-1"),
                        ContentRole =
                            EvidencePackageContentRoles
                                .GeneratedOrganizationalMaterial
                    }
                ]);

        return packageId;
    }
}

public sealed partial class VeteransEvidencePackageConsoleTests
{
    [Fact]
    public async Task EvidencePackage_DisplaysPersistedPackage()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var packageId =
                await SeedPackageAsync(databasePath);

            using var output = new StringWriter();

            var exitCode =
                await VeteransConsoleCommand
                    .RunEvidencePackageAsync(
                        databasePath,
                        packageId,
                        output);

            Assert.Equal(0, exitCode);

            Assert.Equal(
                [
                    "Package: package-1",
                    "Claim Issue: issue-package-1",
                    "Purpose: Physician reviewer package",
                    "Reviewer Role: MedicalProfessional",
                    "Artifacts: 2",
                    "- UnderlyingEvidence: source-1",
                    "- GeneratedOrganizationalMaterial: summary-1"
                ],
                output.ToString().Split(
                    Environment.NewLine,
                    StringSplitOptions.RemoveEmptyEntries));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}

public sealed partial class VeteransEvidencePackageConsoleTests
{
    [Fact]
    public async Task EvidencePackage_ReportsMissingPackage()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            await SeedClaimIssueAsync(databasePath);

            using var output = new StringWriter();

            var exitCode =
                await VeteransConsoleCommand
                    .RunEvidencePackageAsync(
                        databasePath,
                        new EvidencePackageId("missing-package"),
                        output);

            Assert.Equal(1, exitCode);
            Assert.Equal(string.Empty, output.ToString());
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}


public sealed partial class VeteransEvidencePackageConsoleTests
{
    [Fact]
    public async Task EvidencePackage_WritesDocxExport()
    {
        var databasePath = Path.GetTempFileName();
        var outputPath =
            Path.Combine(
                Path.GetTempPath(),
                $"{Guid.NewGuid():N}.docx");

        try
        {
            var packageId =
                await SeedPackageAsync(databasePath);

            var exitCode =
                await VeteransConsoleCommand
                    .RunEvidencePackageDocxAsync(
                        databasePath,
                        packageId,
                        outputPath);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outputPath));
            Assert.True(new FileInfo(outputPath).Length > 0);
        }
        finally
        {
            File.Delete(databasePath);
            File.Delete(outputPath);
        }
    }
}

