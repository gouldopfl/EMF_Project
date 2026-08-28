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
}
