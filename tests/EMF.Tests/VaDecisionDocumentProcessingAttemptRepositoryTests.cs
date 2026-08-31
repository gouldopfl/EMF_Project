using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

namespace EMF.Tests;

public sealed class VaDecisionDocumentProcessingAttemptRepositoryTests
{
    [Fact]
    public async Task Repository_PersistsAttempt()
    {
        var path = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(path)
                .InitializeAsync();

            var veteran = new Veteran
            {
                Id = new VeteranId("veteran-001")
            };

            await new SqliteVeteranRepository(path)
                .AddVeteranAsync(veteran);

            var claim = new Claim
            {
                Id = new ClaimId("claim-001"),
                VeteranId = veteran.Id
            };

            await new SqliteClaimRepository(path)
                .AddClaimAsync(claim);

            var repository =
                new SqliteVaDecisionDocumentProcessingAttemptRepository(
                    path);

            var processedAt =
                new DateTimeOffset(
                    2026, 8, 28, 10, 0, 0,
                    TimeSpan.Zero);

            await repository.AddAsync(
                new VaDecisionDocumentProcessingAttempt
                {
                    ClaimId = new ClaimId("claim-001"),
                    ArtifactId = new ArtifactId("artifact-001"),
                    ProcessedAt = processedAt,
                    VaDecisionId = null,
                    Matches =
                        new[]
                        {
                            new VaDecisionDocumentIssueMatch
                            {
                                Status =
                                    VaDecisionDocumentIssueMatchStatuses.Matched,
                                ClaimIssueId =
                                    new ClaimIssueId("claim-issue-001"),
                                CandidateClaimIssueIds =
                                    new[]
                                    {
                                        new ClaimIssueId("claim-issue-001")
                                    },
                                Interpretation =
                                    new VaIssueDecisionInterpretation
                                    {
                                        IssueDescription = "Sleep apnea",
                                        Outcome = "Denied",
                                        Rationale = "Test rationale",
                                        FavorableFindings =
                                            new[] { "Favorable fact" },
                                        AdverseFindings =
                                            new[] { "Adverse fact" },
                                        CitedRegulations =
                                            new[] { "38 CFR 3.303" },
                                        ReferencedEvidence =
                                            new[] { "Sleep study" },
                                        SourceExcerpts =
                                            new[]
                                            {
                                                new DecisionDocumentSourceExcerpt
                                                {
                                                    ArtifactId =
                                                        new ArtifactId(
                                                            "artifact-001"),
                                                    Text = "Excerpt text",
                                                    StartOffset = 12,
                                                    Length = 24
                                                }
                                            }
                                    }
                            }
                        }
                });

            var stored =
                await repository.GetByClaimAsync(
                    new ClaimId("claim-001"));

            var attempt = Assert.Single(stored);

            Assert.Equal(
                new ArtifactId("artifact-001"),
                attempt.ArtifactId);

            Assert.Equal(
                processedAt,
                attempt.ProcessedAt);

            Assert.Null(attempt.VaDecisionId);

            var match = Assert.Single(attempt.Matches);

            Assert.Equal(
                VaDecisionDocumentIssueMatchStatuses.Matched,
                match.Status);

            Assert.Equal(
                new ClaimIssueId("claim-issue-001"),
                match.ClaimIssueId);

            Assert.Equal(
                "Sleep apnea",
                match.Interpretation.IssueDescription);

            Assert.Equal(
                "Denied",
                match.Interpretation.Outcome);

            Assert.Equal(
                "Test rationale",
                match.Interpretation.Rationale);

            Assert.Equal(
                new ClaimIssueId("claim-issue-001"),
                Assert.Single(
                    match.CandidateClaimIssueIds));

            Assert.Equal(
                "Favorable fact",
                Assert.Single(
                    match.Interpretation.FavorableFindings));

            Assert.Equal(
                "Adverse fact",
                Assert.Single(
                    match.Interpretation.AdverseFindings));

            Assert.Equal(
                "38 CFR 3.303",
                Assert.Single(
                    match.Interpretation.CitedRegulations));

            Assert.Equal(
                "Sleep study",
                Assert.Single(
                    match.Interpretation.ReferencedEvidence));

            var excerpt =
                Assert.Single(
                    match.Interpretation.SourceExcerpts);

            Assert.Equal(
                new ArtifactId("artifact-001"),
                excerpt.ArtifactId);

            Assert.Equal("Excerpt text", excerpt.Text);
            Assert.Equal(12, excerpt.StartOffset);
            Assert.Equal(24, excerpt.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GetByClaimAsync_orders_attempts_by_processed_time()
    {
        var path = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(path)
                .InitializeAsync();

            var veteran =
                new Veteran
                {
                    Id = new VeteranId("veteran-ordering")
                };

            await new SqliteVeteranRepository(path)
                .AddVeteranAsync(veteran);

            var claim =
                new Claim
                {
                    Id = new ClaimId("claim-ordering"),
                    VeteranId = veteran.Id
                };

            await new SqliteClaimRepository(path)
                .AddClaimAsync(claim);

            var repository =
                new SqliteVaDecisionDocumentProcessingAttemptRepository(
                    path);

            await repository.AddAsync(
                new VaDecisionDocumentProcessingAttempt
                {
                    ClaimId = claim.Id,
                    ArtifactId =
                        new ArtifactId("artifact-later"),
                    ProcessedAt =
                        new DateTimeOffset(
                            2026, 8, 28, 12, 0, 0,
                            TimeSpan.Zero),
                    VaDecisionId = null,
                    Matches = []
                });

            await repository.AddAsync(
                new VaDecisionDocumentProcessingAttempt
                {
                    ClaimId = claim.Id,
                    ArtifactId =
                        new ArtifactId("artifact-earlier"),
                    ProcessedAt =
                        new DateTimeOffset(
                            2026, 8, 28, 10, 0, 0,
                            TimeSpan.Zero),
                    VaDecisionId = null,
                    Matches = []
                });

            var stored =
                await repository.GetByClaimAsync(claim.Id);

            Assert.Equal(2, stored.Count);
            Assert.Equal(
                new ArtifactId("artifact-earlier"),
                stored[0].ArtifactId);
            Assert.Equal(
                new ArtifactId("artifact-later"),
                stored[1].ArtifactId);
        }
        finally
        {
            File.Delete(path);
        }
    }


    [Fact]
    public async Task AddAsync_rolls_back_when_nested_write_fails()
    {
        var path =
            Path.Combine(
                Path.GetTempPath(),
                $"{Guid.NewGuid():N}.db");

        try
        {
            await new VeteransClaimsSqliteSchema(path)
                .InitializeAsync();

            var veteran =
                new Veteran
                {
                    Id = new VeteranId("veteran-rollback")
                };

            await new SqliteVeteranRepository(path)
                .AddVeteranAsync(veteran);

            await new SqliteClaimRepository(path)
                .AddClaimAsync(
                    new Claim
                    {
                        Id = new ClaimId("claim-rollback"),
                        VeteranId = veteran.Id
                    });

            var repository =
                new SqliteVaDecisionDocumentProcessingAttemptRepository(
                    path);

            var attempt =
                new VaDecisionDocumentProcessingAttempt
                {
                    ClaimId = new ClaimId("claim-rollback"),
                    ArtifactId = new ArtifactId("artifact-rollback"),
                    ProcessedAt = DateTimeOffset.UtcNow,
                    VaDecisionId = null,
                    Matches =
                    [
                        new VaDecisionDocumentIssueMatch
                        {
                            Status =
                                VaDecisionDocumentIssueMatchStatuses.Matched,
                            ClaimIssueId = null,
                            CandidateClaimIssueIds =
                            [
                                new ClaimIssueId("candidate-rollback")
                            ],
                            Interpretation =
                                new VaIssueDecisionInterpretation
                                {
                                    IssueDescription = "Rollback issue",
                                    Outcome = "Denied",
                                    Rationale = "Rollback test",
                                    FavorableFindings = [],
                                    AdverseFindings = [],
                                    CitedRegulations = [],
                                    ReferencedEvidence = [],
                                    SourceExcerpts =
                                    [
                                        new DecisionDocumentSourceExcerpt
                                        {
                                            ArtifactId =
                                                new ArtifactId(
                                                    "artifact-rollback"),
                                            Text = null!,
                                            StartOffset = null,
                                            Length = null
                                        }
                                    ]
                                }
                        }
                    ]
                };

            await Assert.ThrowsAnyAsync<Exception>(
                () => repository.AddAsync(attempt));

            var stored =
                await repository.GetByClaimAsync(
                    new ClaimId("claim-rollback"));

            Assert.Empty(stored);
        }
        finally
        {
            File.Delete(path);
        }
    }

}
