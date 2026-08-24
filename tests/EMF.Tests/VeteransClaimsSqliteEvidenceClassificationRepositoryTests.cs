using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Regulatory;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

namespace EMF.Tests;

public sealed class VeteransClaimsSqliteEvidenceClassificationRepositoryTests
{
    [Fact]
    public async Task Repository_RoundTripsEvidenceClassifications()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteEvidenceClassificationRepository(
                    databasePath);

            await repository.InitializeAsync();

            var veteran = new Veteran
            {
                Id = new VeteranId("veteran-001")
            };

            await new SqliteVeteranRepository(databasePath)
                .AddVeteranAsync(veteran);

            var claim = new Claim
            {
                Id = new ClaimId("claim-001"),
                VeteranId = veteran.Id
            };

            await new SqliteClaimRepository(databasePath)
                .AddClaimAsync(claim);

            var claimIssue = new ClaimIssue
            {
                Id = new ClaimIssueId("claim-issue-001"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

            await new SqliteClaimIssueRepository(databasePath)
                .AddClaimIssueAsync(claimIssue);

            var artifactId =
                new ArtifactId("artifact-001");

            var classification =
                new EvidenceClassification
                {
                    Id =
                        new EvidenceClassificationId(
                            "classification-001"),
                    ArtifactId = artifactId,
                    ClaimIssueId = claimIssue.Id,
                    Classification =
                        EvidenceClassifications.MedicalEvidence
                };

            await repository
                .AddEvidenceClassificationAsync(
                    classification);

            var stored =
                await repository
                    .GetEvidenceClassificationAsync(
                        classification.Id);

            var byArtifact =
                await repository
                    .GetEvidenceClassificationsAsync(
                        artifactId);

            var byIssue =
                await repository
                    .GetEvidenceClassificationsAsync(
                        claimIssue.Id);

            Assert.NotNull(stored);
            Assert.Equal(classification.Id, stored!.Id);
            Assert.Equal(
                classification.ArtifactId,
                stored.ArtifactId);
            Assert.Equal(
                classification.ClaimIssueId,
                stored.ClaimIssueId);
            Assert.Equal(
                classification.Classification,
                stored.Classification);

            Assert.Equal(
                classification.Id,
                Assert.Single(byArtifact).Id);

            Assert.Equal(
                classification.Id,
                Assert.Single(byIssue).Id);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Repository_PreservesNullClaimIssue()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteEvidenceClassificationRepository(
                    databasePath);

            await repository.InitializeAsync();

            var classification = new EvidenceClassification
            {
                Id = new EvidenceClassificationId(
                    "classification-002"),
                ArtifactId = new ArtifactId("artifact-002"),
                ClaimIssueId = null,
                Classification =
                    EvidenceClassifications.ServiceRecord
            };

            await repository.AddEvidenceClassificationAsync(
                classification);

            var stored =
                await repository.GetEvidenceClassificationAsync(
                    classification.Id);

            Assert.NotNull(stored);
            Assert.Null(stored!.ClaimIssueId);
            Assert.Equal(
                classification.ArtifactId,
                stored.ArtifactId);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
    [Fact]
    public async Task Repository_RejectsDuplicateSemanticClassification()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteEvidenceClassificationRepository(
                    databasePath);

            await repository.InitializeAsync();

            var first =
                new EvidenceClassification
                {
                    Id =
                        new EvidenceClassificationId(
                            "classification-dup-1"),
                    ArtifactId =
                        new ArtifactId("artifact-dup"),
                    Classification =
                        EvidenceClassifications.MedicalEvidence
                };

            var second =
                new EvidenceClassification
                {
                    Id =
                        new EvidenceClassificationId(
                            "classification-dup-2"),
                    ArtifactId =
                        first.ArtifactId,
                    Classification =
                        first.Classification
                };

            await repository
                .AddEvidenceClassificationAsync(first);

            await Assert.ThrowsAsync<
                Microsoft.Data.Sqlite.SqliteException>(
                () =>
                    repository
                        .AddEvidenceClassificationAsync(
                            second));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Repository_RoundTripsClassificationRequirement()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteEvidenceClassificationRepository(
                    databasePath);

            await repository.InitializeAsync();

            var classification =
                new EvidenceClassification
                {
                    Id =
                        new EvidenceClassificationId(
                            "classification-requirement-1"),
                    ArtifactId =
                        new ArtifactId(
                            "artifact-requirement-1"),
                    Classification =
                        EvidenceClassifications.MedicalEvidence
                };

            await repository
                .AddEvidenceClassificationAsync(
                    classification);

            var regulatory =
                new SqliteRegulatoryRepository(
                    databasePath);

            var authority =
                new RegulatoryAuthority
                {
                    Id =
                        new RegulatoryAuthorityId(
                            "authority-requirement-1"),
                    AuthorityType = "Regulation",
                    Citation = "38 CFR",
                    Title = "Test Regulation"
                };

            await regulatory.AddRegulatoryAuthorityAsync(
                authority);

            var provision =
                new RegulatoryProvision
                {
                    Id =
                        new RegulatoryProvisionId(
                            "provision-requirement-1"),
                    RegulatoryAuthorityId = authority.Id,
                    ProvisionType =
                        RegulatoryProvisionTypes.Requirement,
                    Citation = "38 CFR"
                };

            await regulatory.AddRegulatoryProvisionAsync(
                provision);

            var requirement =
                new Requirement
                {
                    Id =
                        new RequirementId(
                            "requirement-trace-1"),
                    RegulatoryProvisionId = provision.Id,
                    Description = "Required evidence."
                };

            await regulatory.AddRequirementAsync(
                requirement);

            await repository
                .AddEvidenceClassificationRequirementAsync(
                    new EvidenceClassificationRequirement
                    {
                        EvidenceClassificationId =
                            classification.Id,
                        RequirementId =
                            requirement.Id
                    });

            var stored =
                await repository
                    .GetEvidenceClassificationRequirementsAsync(
                        classification.Id);

            var byRequirement =
                await repository
                    .GetEvidenceClassificationsAsync(
                        requirement.Id);

            var association = Assert.Single(stored);

            Assert.Equal(
                classification.Id,
                association.EvidenceClassificationId);
            Assert.Equal(
                requirement.Id,
                association.RequirementId);

            Assert.Equal(
                classification.Id,
                Assert.Single(byRequirement).Id);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }


}
