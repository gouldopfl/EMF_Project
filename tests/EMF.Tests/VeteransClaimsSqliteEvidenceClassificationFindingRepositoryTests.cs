using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Persistence.Repositories;

namespace EMF.Tests;

public sealed class VeteransClaimsSqliteEvidenceClassificationFindingRepositoryTests
{
    [Fact]
    public async Task Association_RoundTrips()
    {
        var path = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();

            var evidence = new SqliteEvidenceRepository(path);
            await evidence.InitializeAsync();

            var veteran = new Veteran { Id = new VeteranId("v-ecf") };
            await new SqliteVeteranRepository(path).AddVeteranAsync(veteran);

            var claim = new Claim
            {
                Id = new ClaimId("claim-ecf"),
                VeteranId = veteran.Id
            };
            await new SqliteClaimRepository(path).AddClaimAsync(claim);

            var issue = new ClaimIssue
            {
                Id = new ClaimIssueId("issue-ecf"),
                ClaimId = claim.Id,
                ClaimIssueType = ClaimIssueTypes.ServiceConnection
            };
            await new SqliteClaimIssueRepository(path).AddClaimIssueAsync(issue);

            var artifact = new Artifact
            {
                Id = new ArtifactId("artifact-ecf"),
                Name = "Test evidence",
                ArtifactType = "Test"
            };
            await evidence.AddArtifactAsync(artifact);

            var repository =
                new SqliteEvidenceClassificationRepository(path);

            var classification = new EvidenceClassification
            {
                Id = new EvidenceClassificationId("class-ecf"),
                ArtifactId = artifact.Id,
                ClaimIssueId = issue.Id,
                Classification = "MedicalEvidence"
            };
            await repository.AddEvidenceClassificationAsync(classification);

            var finding = new Finding
            {
                Id = new FindingId("finding-ecf"),
                ClaimIssueId = issue.Id,
                RequirementId = null,
                Outcome = FindingOutcomes.Favorable,
                Description = "Supported finding"
            };
            await new SqliteFindingRepository(path).AddFindingAsync(finding);

            await repository.AddEvidenceClassificationFindingAsync(
                new EvidenceClassificationFinding
                {
                    EvidenceClassificationId = classification.Id,
                    FindingId = finding.Id
                });

            Assert.Single(
                await repository.GetEvidenceClassificationFindingsAsync(
                    classification.Id));

            Assert.Single(
                await repository.GetEvidenceClassificationsAsync(
                    finding.Id));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Association_RejectsDifferentClaimIssue()
    {
        var path = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();

            var evidence = new SqliteEvidenceRepository(path);
            await evidence.InitializeAsync();

            var veteran = new Veteran
            {
                Id = new VeteranId("v-ecf-mismatch")
            };
            await new SqliteVeteranRepository(path)
                .AddVeteranAsync(veteran);

            var claim = new Claim
            {
                Id = new ClaimId("claim-ecf-mismatch"),
                VeteranId = veteran.Id
            };
            await new SqliteClaimRepository(path)
                .AddClaimAsync(claim);

            var firstIssue = new ClaimIssue
            {
                Id = new ClaimIssueId("issue-ecf-a"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

            var secondIssue = new ClaimIssue
            {
                Id = new ClaimIssueId("issue-ecf-b"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

            var issues = new SqliteClaimIssueRepository(path);
            await issues.AddClaimIssueAsync(firstIssue);
            await issues.AddClaimIssueAsync(secondIssue);

            var artifact = new Artifact
            {
                Id = new ArtifactId("artifact-ecf-mismatch"),
                Name = "Test evidence",
                ArtifactType = "Test"
            };
            await evidence.AddArtifactAsync(artifact);

            var repository =
                new SqliteEvidenceClassificationRepository(path);

            var classification = new EvidenceClassification
            {
                Id = new EvidenceClassificationId("class-ecf-mismatch"),
                ArtifactId = artifact.Id,
                ClaimIssueId = firstIssue.Id,
                Classification = "MedicalEvidence"
            };
            await repository.AddEvidenceClassificationAsync(
                classification);

            var finding = new Finding
            {
                Id = new FindingId("finding-ecf-mismatch"),
                ClaimIssueId = secondIssue.Id,
                RequirementId = null,
                Outcome = FindingOutcomes.Unfavorable,
                Description = "Different claim issue"
            };
            await new SqliteFindingRepository(path)
                .AddFindingAsync(finding);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => repository.AddEvidenceClassificationFindingAsync(
                    new EvidenceClassificationFinding
                    {
                        EvidenceClassificationId =
                            classification.Id,
                        FindingId = finding.Id
                    }));
        }
        finally
        {
            File.Delete(path);
        }
    }


    [Fact]
    public async Task Association_AllowsUnscopedClassification()
    {
        var path = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();

            var evidence = new SqliteEvidenceRepository(path);
            await evidence.InitializeAsync();

            var veteran = new Veteran
            {
                Id = new VeteranId("v-ecf-unscoped")
            };
            await new SqliteVeteranRepository(path)
                .AddVeteranAsync(veteran);

            var claim = new Claim
            {
                Id = new ClaimId("claim-ecf-unscoped"),
                VeteranId = veteran.Id
            };
            await new SqliteClaimRepository(path)
                .AddClaimAsync(claim);

            var issue = new ClaimIssue
            {
                Id = new ClaimIssueId("issue-ecf-unscoped"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };
            await new SqliteClaimIssueRepository(path)
                .AddClaimIssueAsync(issue);

            var artifact = new Artifact
            {
                Id = new ArtifactId("artifact-ecf-unscoped"),
                Name = "Unscoped evidence",
                ArtifactType = "Test"
            };
            await evidence.AddArtifactAsync(artifact);

            var repository =
                new SqliteEvidenceClassificationRepository(path);

            var classification = new EvidenceClassification
            {
                Id = new EvidenceClassificationId("class-ecf-unscoped"),
                ArtifactId = artifact.Id,
                ClaimIssueId = null,
                Classification = "MedicalEvidence"
            };
            await repository.AddEvidenceClassificationAsync(
                classification);

            var finding = new Finding
            {
                Id = new FindingId("finding-ecf-unscoped"),
                ClaimIssueId = issue.Id,
                RequirementId = null,
                Outcome = FindingOutcomes.Favorable,
                Description = "Finding using unscoped evidence"
            };
            await new SqliteFindingRepository(path)
                .AddFindingAsync(finding);

            await repository.AddEvidenceClassificationFindingAsync(
                new EvidenceClassificationFinding
                {
                    EvidenceClassificationId = classification.Id,
                    FindingId = finding.Id
                });

            Assert.Single(
                await repository.GetEvidenceClassificationFindingsAsync(
                    classification.Id));
        }
        finally
        {
            File.Delete(path);
        }
    }

}
