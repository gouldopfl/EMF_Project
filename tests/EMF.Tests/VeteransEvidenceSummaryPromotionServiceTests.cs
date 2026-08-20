using EMF.Core.Models.Identities;
using EMF.Intelligence.Agents;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;
using EMF.Persistence.Repositories;

namespace EMF.Tests;

public sealed class VeteransEvidenceSummaryPromotionServiceTests
{
    [Fact]
    public async Task PromoteAsync_PersistsSummaryAndLineage()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteEvidenceRepository(databasePath);

            await repository.InitializeAsync();

            var occurredUtc =
                new DateTimeOffset(
                    2026, 8, 20, 20, 0, 0,
                    TimeSpan.Zero);

            var result =
                new IntelligenceAgentResult<string>
                {
                    Success = true,
                    Output = "Summary.",
                    AgentId =
                        new AgentId("summary-agent"),
                    CorrelationId =
                        new IntelligenceCorrelationId(
                            "summary-operation"),
                    StartedUtc = occurredUtc,
                    CompletedUtc =
                        occurredUtc.AddSeconds(1),
                    RequiresReview = true,
                    SourceArtifactIds =
                    [
                        new ArtifactId("source-001")
                    ],
                    CapabilityExecutions =
                    [
                        new IntelligenceExecutionMetadata
                        {
                            CapabilityId =
                                new IntelligenceCapabilityId(
                                    "summarize"),
                            ProviderId =
                                new IntelligenceProviderId(
                                    "test"),
                            CorrelationId =
                                new IntelligenceCorrelationId(
                                    "summary-operation"),
                            EngineName = "test",
                            StartedUtc = occurredUtc,
                            CompletedUtc =
                                occurredUtc.AddSeconds(1)
                        }
                    ]
                };

            var service =
                new VeteransEvidenceSummaryPromotionService(
                    new IntelligenceEvidencePromotionService(
                        repository));

            var artifact =
                await service.PromoteAsync(
                    "Veterans summary",
                    "console-test",
                    "reviewer-test",
                    occurredUtc.AddSeconds(2),
                    result);

            Assert.NotNull(
                await repository.GetArtifactAsync(
                    artifact.Id));

            Assert.Single(
                await repository.GetProvenanceAsync(
                    artifact.Id));

            Assert.Single(
                await repository.GetRelationshipsAsync(
                    artifact.Id));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task PromoteAsync_RejectsRequiredReviewWithoutReviewer()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteEvidenceRepository(databasePath);

            await repository.InitializeAsync();

            var occurredUtc =
                new DateTimeOffset(
                    2026, 8, 20, 20, 0, 0,
                    TimeSpan.Zero);

            var result =
                new IntelligenceAgentResult<string>
                {
                    Success = true,
                    Output = "Summary.",
                    AgentId =
                        new AgentId("summary-agent"),
                    CorrelationId =
                        new IntelligenceCorrelationId(
                            "summary-operation"),
                    StartedUtc = occurredUtc,
                    CompletedUtc =
                        occurredUtc.AddSeconds(1),
                    RequiresReview = true,
                    SourceArtifactIds =
                    [
                        new ArtifactId("source-001")
                    ],
                    CapabilityExecutions =
                    [
                        new IntelligenceExecutionMetadata
                        {
                            CapabilityId =
                                new IntelligenceCapabilityId(
                                    "summarize"),
                            ProviderId =
                                new IntelligenceProviderId(
                                    "test"),
                            CorrelationId =
                                new IntelligenceCorrelationId(
                                    "summary-operation"),
                            EngineName = "test",
                            StartedUtc = occurredUtc,
                            CompletedUtc =
                                occurredUtc.AddSeconds(1)
                        }
                    ]
                };

            var service =
                new VeteransEvidenceSummaryPromotionService(
                    new IntelligenceEvidencePromotionService(
                        repository));

            await Assert.ThrowsAsync<
                IntelligenceEvidencePromotionException>(
                () => service.PromoteAsync(
                    "Veterans summary",
                    "console-test",
                    "",
                    occurredUtc.AddSeconds(2),
                    result));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}
