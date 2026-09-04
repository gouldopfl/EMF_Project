using DocumentFormat.OpenXml.Packaging;
using EMF.Core.Models;
using EMF.Persistence.Repositories;
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
    public async Task EvidencePackage_RejectsDatabaseAsDocxOutput()
    {
        var databasePath =
            Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(
                    databasePath)
                .InitializeAsync();

            var originalContent =
                await File.ReadAllBytesAsync(
                    databasePath);

            var exitCode =
                await VeteransConsoleCommand
                    .RunEvidencePackageDocxAsync(
                        databasePath,
                        new EvidencePackageId("package-1"),
                        databasePath);

            Assert.Equal(2, exitCode);
            Assert.Equal(
                originalContent,
                await File.ReadAllBytesAsync(
                    databasePath));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

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

        var evidence =
            new SqliteEvidenceRepository(databasePath);

        await evidence.InitializeAsync();

        await evidence.AddArtifactAsync(
            new Artifact
            {
                Id = new ArtifactId("source-1"),
                Name = "Sleep Study",
                ArtifactType = "text-summary",
                Metadata =
                    new Dictionary<string, object>
                    {
                        ["summary"] =
                            "Severe obstructive sleep apnea documented."
                    }
            });

        await evidence.AddArtifactAsync(
            new Artifact
            {
                Id = new ArtifactId("summary-1"),
                Name = "Reviewer Summary",
                ArtifactType = "text-summary",
                Metadata =
                    new Dictionary<string, object>
                    {
                        ["summary"] =
                            "Generated reviewer summary."
                    }
            });

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
                    "- GeneratedOrganizationalMaterial: summary-1",
                    "Artifact Details:",
                    "- source-1: Sleep Study [text-summary]",
                    "- summary-1: Reviewer Summary [text-summary]",
                    "Artifact Content: Sleep Study [source-1]",
                    "Severe obstructive sleep apnea documented.",
                    "Artifact Content: Reviewer Summary [summary-1]",
                    "Generated reviewer summary."
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


public sealed partial class VeteransEvidencePackageConsoleTests
{
    [Fact]
    public async Task EvidencePackage_WritesDocxFromCommand()
    {
        var databasePath =
            Path.Combine(
                Path.GetTempPath(),
                $"emf-package-{Guid.NewGuid():N}.db");

        var outputPath =
            Path.ChangeExtension(
                databasePath,
                ".docx");

        try
        {
            var issueId =
                await SeedClaimIssueAsync(
                    databasePath);

            var packages =
                new SqliteEvidencePackageRepository(
                    databasePath);

            var package =
                new EvidencePackage
                {
                    Id =
                        new EvidencePackageId(
                            "package-docx-1"),
                    ClaimIssueId = issueId,
                    Purpose = "Medical review",
                    ReviewerRole = "MedicalProfessional"
                };

            await packages.AddEvidencePackageAsync(
                package,
                []);

            var exitCode =
                await VeteransConsoleCommand.RunAsync(
                    [
                        "evidence",
                        "package",
                        databasePath,
                        package.Id.Value,
                        outputPath
                    ]);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outputPath));
            Assert.True(
                new FileInfo(outputPath).Length > 0);
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);

            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }
}

public sealed partial class VeteransEvidencePackageConsoleTests
{
    [Fact]
    public async Task EvidencePackage_WritesMedicalEvidenceAppendix()
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

            var classifications =
                new SqliteEvidenceClassificationRepository(
                    databasePath);

            await classifications.AddEvidenceClassificationAsync(
                new EvidenceClassification
                {
                    Id =
                        new EvidenceClassificationId(
                            "classification-1"),
                    ArtifactId =
                        new ArtifactId("source-1"),
                    ClaimIssueId =
                        new ClaimIssueId("issue-package-1"),
                    Classification =
                        EvidenceClassifications.MedicalEvidence
                });

            var exitCode =
                await VeteransConsoleCommand
                    .RunEvidencePackageDocxAsync(
                        databasePath,
                        packageId,
                        outputPath);

            Assert.Equal(0, exitCode);

            using var document =
                WordprocessingDocument.Open(
                    outputPath,
                    false);

            var mainPart =
                Assert.IsType<MainDocumentPart>(
                    document.MainDocumentPart);

            var documentRoot =
                mainPart.Document;

            Assert.NotNull(documentRoot);

            var body =
                documentRoot!.Body;

            Assert.NotNull(body);

            var text =
                body!.InnerText;

            Assert.Contains(
                "Appendix A — Medical Evidence",
                text);
        }
        finally
        {
            File.Delete(databasePath);
            File.Delete(outputPath);
        }
    }
}
