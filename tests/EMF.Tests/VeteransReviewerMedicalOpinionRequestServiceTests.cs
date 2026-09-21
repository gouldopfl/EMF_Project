using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Conditions;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Extensions.VeteransClaims.Regulatory;

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
                result.OpinionText);
            Assert.Contains(
                "If causation is not established",
                result.OpinionText);
            Assert.Contains(
                "Obstructive Sleep Apnea is at least as likely as not " +
                "aggravated by the service-connected PTSD",
                result.OpinionText);
            Assert.Contains(
                "supporting medical rationale",
                result.OpinionText);
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
            Assert.Contains("Anxiety", result.OpinionText);
            Assert.Contains("Major Depressive Disorder", result.OpinionText);
            Assert.Contains("PTSD", result.OpinionText);
            Assert.Contains(
                "service-connected Anxiety, Major Depressive Disorder, and PTSD",
                result.OpinionText);
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
                result.OpinionText);
            Assert.DoesNotContain(
                "coronary",
                result.OpinionText,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GetAsync_MedicationBasisCombinesSiblingMedicationBasesUnderSameTheory()
    {
        var path = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();

            var seeded =
                await SeedAsync(
                    path,
                    "combined-medications",
                    ServiceConnectionTheoryTypes.Secondary,
                    "Gastroesophageal reflux disease",
                    ["Coronary artery disease"]);

            var connections =
                new SqliteServiceConnectionRepository(path);
            var selectedBasis =
                await connections.GetServiceConnectionBasisAsync(
                    seeded.BasisId);

            Assert.NotNull(selectedBasis);

            await SetBasisReviewerLabelAsync(
                path,
                seeded.BasisId,
                "Secondary to medications used for service-connected coronary artery disease");

            await connections.AddBasisPrescribedMedicationAsync(
                new ServiceConnectionBasisPrescribedMedication
                {
                    ServiceConnectionBasisId = seeded.BasisId,
                    MedicationName = "Isosorbide Mononitrate"
                });

            var claimedConditionId =
                Assert.Single(
                    await connections.GetClaimedConditionIdsAsync(
                        seeded.BasisId));

            var mentalHealthBasis =
                new ServiceConnectionBasis
                {
                    Id =
                        new ServiceConnectionBasisId(
                            "basis-combined-medications-mental-health"),
                    ClaimIssueId = seeded.IssueId,
                    ServiceConnectionTheoryId =
                        selectedBasis.ServiceConnectionTheoryId,
                    ReviewerLabel =
                        "Secondary to medications used for service-connected PTSD / Anxiety / Major Depression"
                };

            await connections.AddServiceConnectionBasisAsync(
                mentalHealthBasis);

            await connections.AddBasisClaimedConditionAsync(
                new ServiceConnectionBasisClaimedCondition
                {
                    ServiceConnectionBasisId = mentalHealthBasis.Id,
                    ClaimedConditionId = claimedConditionId
                });

            var conditions = new SqliteConditionRepository(path);
            var mentalHealthCondition =
                new MedicalCondition
                {
                    Id =
                        new MedicalConditionId(
                            "service-connected-combined-medications-mental-health"),
                    Name = "PTSD"
                };

            await conditions.AddMedicalConditionAsync(
                mentalHealthCondition);

            await conditions.AddVeteranMedicalConditionAsync(
                new VeteranMedicalCondition
                {
                    VeteranId =
                        new VeteranId(
                            "veteran-combined-medications"),
                    MedicalConditionId = mentalHealthCondition.Id
                });

            await connections.AddBasisServiceConnectedConditionAsync(
                new ServiceConnectionBasisServiceConnectedCondition
                {
                    ServiceConnectionBasisId = mentalHealthBasis.Id,
                    ServiceConnectedConditionId = mentalHealthCondition.Id
                });

            await connections.AddBasisPrescribedMedicationAsync(
                new ServiceConnectionBasisPrescribedMedication
                {
                    ServiceConnectionBasisId = mentalHealthBasis.Id,
                    MedicationName = "Sertraline HCl"
                });

            var result =
                await CreateService(path).GetAsync(
                    Package(seeded.IssueId, seeded.BasisId));

            Assert.NotNull(result);
            Assert.Contains(
                "proximately due to or the result of one or more medications " +
                "prescribed for the Veteran's service-connected coronary artery disease " +
                "and/or one or more medications prescribed for the Veteran's " +
                "service-connected PTSD / Anxiety / Major Depression",
                result.OpinionText);
            Assert.Contains(
                "aggravated by one or more of those medications",
                result.OpinionText);
            Assert.DoesNotContain(
                "service-connected Secondary to medications",
                result.OpinionText,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GetAsync_NonMedicationBasisDoesNotPullSiblingMedicationBasis()
    {
        var path = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();

            var seeded =
                await SeedAsync(
                    path,
                    "non-medication-selected",
                    ServiceConnectionTheoryTypes.Secondary,
                    "Obstructive Sleep Apnea",
                    ["PTSD"]);

            var connections =
                new SqliteServiceConnectionRepository(path);
            var selectedBasis =
                await connections.GetServiceConnectionBasisAsync(
                    seeded.BasisId);

            Assert.NotNull(selectedBasis);

            var cadBasis =
                new ServiceConnectionBasis
                {
                    Id =
                        new ServiceConnectionBasisId(
                            "basis-non-medication-selected-cad"),
                    ClaimIssueId = seeded.IssueId,
                    ServiceConnectionTheoryId =
                        selectedBasis.ServiceConnectionTheoryId,
                    ReviewerLabel =
                        "Secondary to medications used for service-connected coronary artery disease"
                };

            await connections.AddServiceConnectionBasisAsync(cadBasis);

            var conditions = new SqliteConditionRepository(path);
            var cadCondition =
                new MedicalCondition
                {
                    Id =
                        new MedicalConditionId(
                            "service-connected-non-medication-selected-cad"),
                    Name = "Coronary artery disease"
                };

            await conditions.AddMedicalConditionAsync(cadCondition);

            await conditions.AddVeteranMedicalConditionAsync(
                new VeteranMedicalCondition
                {
                    VeteranId =
                        new VeteranId(
                            "veteran-non-medication-selected"),
                    MedicalConditionId = cadCondition.Id
                });

            await connections.AddBasisServiceConnectedConditionAsync(
                new ServiceConnectionBasisServiceConnectedCondition
                {
                    ServiceConnectionBasisId = cadBasis.Id,
                    ServiceConnectedConditionId = cadCondition.Id
                });

            await connections.AddBasisPrescribedMedicationAsync(
                new ServiceConnectionBasisPrescribedMedication
                {
                    ServiceConnectionBasisId = cadBasis.Id,
                    MedicationName = "Isosorbide Mononitrate"
                });

            var result =
                await CreateService(path).GetAsync(
                    Package(seeded.IssueId, seeded.BasisId));

            Assert.NotNull(result);
            Assert.Contains(
                "service-connected PTSD",
                result.OpinionText);
            Assert.DoesNotContain(
                "coronary",
                result.OpinionText,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                "medications prescribed",
                result.OpinionText,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GetAsync_SecondaryIncludesPersistedRegulatoryCitations()
    {
        var path = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();

            var seeded =
                await SeedAsync(
                    path,
                    "regulatory",
                    ServiceConnectionTheoryTypes.Secondary,
                    "GERD",
                    ["Coronary artery disease"]);

            var regulatory = new SqliteRegulatoryRepository(path);
            var connections = new SqliteServiceConnectionRepository(path);
            var authorityId = new RegulatoryAuthorityId("authority-38-cfr");

            await regulatory.AddRegulatoryAuthorityAsync(
                new RegulatoryAuthority
                {
                    Id = authorityId,
                    AuthorityType = "FederalRegulation",
                    Citation = "38 C.F.R.",
                    Title = "Pensions, Bonuses, and Veterans' Relief"
                });

            foreach (var item in new[]
            {
                (Suffix: "a", Citation: "38 C.F.R. § 3.310(a)"),
                (Suffix: "b", Citation: "38 C.F.R. § 3.310(b)")
            })
            {
                var provisionId =
                    new RegulatoryProvisionId($"provision-3-310-{item.Suffix}");
                var requirementId =
                    new RequirementId($"requirement-3-310-{item.Suffix}");

                await regulatory.AddRegulatoryProvisionAsync(
                    new RegulatoryProvision
                    {
                        Id = provisionId,
                        RegulatoryAuthorityId = authorityId,
                        ProvisionType = RegulatoryProvisionTypes.Requirement,
                        Citation = item.Citation
                    });

                await regulatory.AddRequirementAsync(
                    new Requirement
                    {
                        Id = requirementId,
                        RegulatoryProvisionId = provisionId,
                        Description = "Secondary service connection requirement."
                    });

                await connections.AddBasisRequirementAsync(
                    new ServiceConnectionBasisRequirement
                    {
                        ServiceConnectionBasisId = seeded.BasisId,
                        RequirementId = requirementId
                    });
            }

            var result =
                await CreateService(path).GetAsync(
                    Package(seeded.IssueId, seeded.BasisId));

            Assert.NotNull(result);
            Assert.Equal(
                ["38 C.F.R. § 3.310(a)", "38 C.F.R. § 3.310(b)"],
                result.ApplicableRegulatoryCitations);
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

    private static async Task SetBasisReviewerLabelAsync(
        string path,
        ServiceConnectionBasisId basisId,
        string reviewerLabel)
    {
        await using var connection =
            new Microsoft.Data.Sqlite.SqliteConnection(
                $"Data Source={path}");

        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE VeteransClaims_ServiceConnectionBases
            SET ReviewerLabel = $label
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$label", reviewerLabel);
        command.Parameters.AddWithValue("$id", basisId.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static VeteransReviewerMedicalOpinionRequestService CreateService(
        string path) =>
        new(
            new SqliteServiceConnectionRepository(path),
            new SqliteConditionRepository(path),
            new SqliteRegulatoryRepository(path));

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
