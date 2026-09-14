using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Conditions;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

namespace EMF.Tests;

public sealed class VeteransReviewerMedicalOpinionRequestServiceTests
{
    [Fact]
    public async Task GetAsync_SecondaryRequestsCausationAndAggravation()
    {
        var path = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();

            var seeded =
                await SeedAsync(
                    path,
                    "1",
                    ServiceConnectionTheoryTypes.Secondary,
                    "Obstructive Sleep Apnea",
                    ["PTSD"]);

            var result =
                await CreateService(path).GetAsync(
                    Package(seeded.IssueId, seeded.BasisId));

            Assert.NotNull(result);
            Assert.Contains(
                "Obstructive Sleep Apnea is at least as likely as not " +
                "(50 percent or greater probability) proximately due to " +
                "or the result of the Veteran's service-connected PTSD",
                result);
            Assert.Contains(
                "If causation is not established",
                result);
            Assert.Contains(
                "Obstructive Sleep Apnea is at least as likely as not " +
                "aggravated by the service-connected PTSD",
                result);
            Assert.Contains(
                "supporting medical rationale",
                result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GetAsync_SecondaryIncludesMultipleServiceConnectedConditions()
    {
        var path = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();

            var seeded =
                await SeedAsync(
                    path,
                    "1",
                    ServiceConnectionTheoryTypes.Secondary,
                    "Obstructive Sleep Apnea",
                    ["PTSD", "Major Depressive Disorder", "Anxiety"]);

            var result =
                await CreateService(path).GetAsync(
                    Package(seeded.IssueId, seeded.BasisId));

            Assert.NotNull(result);
            Assert.Contains("Anxiety", result);
            Assert.Contains("Major Depressive Disorder", result);
            Assert.Contains("PTSD", result);
            Assert.Contains(
                "service-connected Anxiety, Major Depressive Disorder, and PTSD",
                result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GetAsync_SecondaryUsesReviewerBasisLabelWhenPresent()
    {
        var path = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();

            var seeded =
                await SeedAsync(
                    path,
                    "reviewer-label",
                    ServiceConnectionTheoryTypes.Secondary,
                    "Obstructive Sleep Apnea",
                    ["Major depressive disorder with anxious distress to include mild neurocognitive disorder"]);

            await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                $"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    UPDATE VeteransClaims_ServiceConnectionBases
                    SET ReviewerLabel = $label
                    WHERE Id = $id;
                    """;
                command.Parameters.AddWithValue(
                    "$label",
                    "psychiatric disability, including PTSD, anxiety, and major depressive disorder");
                command.Parameters.AddWithValue(
                    "$id",
                    seeded.BasisId.Value);
                await command.ExecuteNonQueryAsync();
            }

            var result =
                await CreateService(path).GetAsync(
                    Package(seeded.IssueId, seeded.BasisId));

            Assert.NotNull(result);
            Assert.Contains(
                "service-connected psychiatric disability, including PTSD, anxiety, and major depressive disorder",
                result);
            Assert.DoesNotContain("coronary", result, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GetAsync_NonSecondaryReturnsNull()
    {
        var path = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();

            var seeded =
                await SeedAsync(
                    path,
                    "1",
                    ServiceConnectionTheoryTypes.Direct,
                    "Lumbar Degenerative Disc Disease",
                    []);

            var result =
                await CreateService(path).GetAsync(
                    Package(seeded.IssueId, seeded.BasisId));

            Assert.Null(result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GetAsync_RejectsBasisFromAnotherClaimIssue()
    {
        var path = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();

            var first =
                await SeedAsync(
                    path,
                    "1",
                    ServiceConnectionTheoryTypes.Secondary,
                    "Obstructive Sleep Apnea",
                    ["PTSD"]);

            var second =
                await SeedAsync(
                    path,
                    "2",
                    ServiceConnectionTheoryTypes.Secondary,
                    "GERD",
                    ["PTSD"]);

            var ex =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => CreateService(path).GetAsync(
                        Package(first.IssueId, second.BasisId)));

            Assert.Equal(
                "Reviewer package service-connection basis lineage mismatch.",
                ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static VeteransReviewerMedicalOpinionRequestService CreateService(
        string path) =>
        new(
            new SqliteServiceConnectionRepository(path),
            new SqliteConditionRepository(path));

    private static EvidencePackage Package(
        ClaimIssueId issueId,
        ServiceConnectionBasisId? basisId) =>
        new()
        {
            Id = new EvidencePackageId("package-1"),
            ClaimIssueId = issueId,
            Purpose = "Medical review",
            ReviewerRole = "MedicalProfessional",
            ServiceConnectionBasisId = basisId
        };

    private static async Task<(
        ClaimIssueId IssueId,
        ServiceConnectionBasisId BasisId)> SeedAsync(
            string path,
            string suffix,
            string theoryType,
            string claimedConditionName,
            IReadOnlyList<string> serviceConnectedConditionNames)
    {
        var veteran =
            new Veteran
            {
                Id = new VeteranId($"veteran-{suffix}")
            };

        await new SqliteVeteranRepository(path)
            .AddVeteranAsync(veteran);

        var claim =
            new Claim
            {
                Id = new ClaimId($"claim-{suffix}"),
                VeteranId = veteran.Id
            };

        await new SqliteClaimRepository(path)
            .AddClaimAsync(claim);

        var issue =
            new ClaimIssue
            {
                Id = new ClaimIssueId($"issue-{suffix}"),
                ClaimId = claim.Id,
                ClaimIssueType = ClaimIssueTypes.ServiceConnection
            };

        await new SqliteClaimIssueRepository(path)
            .AddClaimIssueAsync(issue);

        var connections =
            new SqliteServiceConnectionRepository(path);

        var theory =
            new ServiceConnectionTheory
            {
                Id = new ServiceConnectionTheoryId($"theory-{suffix}"),
                ClaimIssueId = issue.Id,
                TheoryType = theoryType
            };

        await connections.AddServiceConnectionTheoryAsync(theory);

        var basis =
            new ServiceConnectionBasis
            {
                Id = new ServiceConnectionBasisId($"basis-{suffix}"),
                ClaimIssueId = issue.Id,
                ServiceConnectionTheoryId = theory.Id
            };

        await connections.AddServiceConnectionBasisAsync(basis);

        var conditions =
            new SqliteConditionRepository(path);

        var claimedCondition =
            new ClaimedCondition
            {
                Id = new ClaimedConditionId($"claimed-{suffix}"),
                ClaimIssueId = issue.Id,
                Name = claimedConditionName
            };

        await conditions.AddClaimedConditionAsync(claimedCondition);

        await connections.AddBasisClaimedConditionAsync(
            new ServiceConnectionBasisClaimedCondition
            {
                ServiceConnectionBasisId = basis.Id,
                ClaimedConditionId = claimedCondition.Id
            });

        for (var i = 0; i < serviceConnectedConditionNames.Count; i++)
        {
            var medicalCondition =
                new MedicalCondition
                {
                    Id =
                        new MedicalConditionId(
                            $"service-connected-{suffix}-{i + 1}"),
                    Name = serviceConnectedConditionNames[i]
                };

            await conditions.AddMedicalConditionAsync(medicalCondition);

            await conditions.AddVeteranMedicalConditionAsync(
                new VeteranMedicalCondition
                {
                    VeteranId = veteran.Id,
                    MedicalConditionId = medicalCondition.Id
                });

            await connections.AddBasisServiceConnectedConditionAsync(
                new ServiceConnectionBasisServiceConnectedCondition
                {
                    ServiceConnectionBasisId = basis.Id,
                    ServiceConnectedConditionId = medicalCondition.Id
                });
        }

        return (issue.Id, basis.Id);
    }
}
