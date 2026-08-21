using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;
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
                    new EvidenceGapId("gap-1"),
                    new RequirementId("req-1"),
                    result);

            var stored =
                await repository.GetArtifactAsync(
                    artifact.Id);

            Assert.NotNull(stored);
            Assert.Equal(
                "gap-1",
                stored!.Metadata["evidenceGapId"].ToString());
            Assert.Equal(
                "req-1",
                stored.Metadata["requirementId"].ToString());

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
                    new EvidenceGapId("gap-1"),
                    new RequirementId("req-1"),
                    result));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task PromoteAsync_PersistsLineageQueryableByEvidenceLineageService()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteEvidenceRepository(databasePath);

            await repository.InitializeAsync();

            var source =
                new Artifact
                {
                    Id = new ArtifactId("lineage-source-001"),
                    Name = "Source evidence",
                    ArtifactType = "file"
                };

            await repository.AddArtifactAsync(source);

            var occurredUtc =
                new DateTimeOffset(
                    2026, 8, 21, 14, 0, 0,
                    TimeSpan.Zero);

            var result =
                new IntelligenceAgentResult<string>
                {
                    Success = true,
                    Output = "Lineage summary.",
                    AgentId =
                        new AgentId("summary-agent"),
                    CorrelationId =
                        new IntelligenceCorrelationId(
                            "lineage-operation"),
                    StartedUtc = occurredUtc,
                    CompletedUtc =
                        occurredUtc.AddSeconds(1),
                    RequiresReview = true,
                    SourceArtifactIds =
                    [
                        source.Id
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
                                    "lineage-operation"),
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

            var generated =
                await service.PromoteAsync(
                    "Lineage summary",
                    "console-test",
                    "reviewer-test",
                    occurredUtc.AddSeconds(2),
                    new EvidenceGapId("lineage-gap"),
                    new RequirementId("lineage-requirement"),
                    result);

            var lineageService =
                new EvidenceLineageService(repository);

            var roots =
                await lineageService.GetGeneratedFromRootsAsync(
                    generated.Id);

            var root = Assert.Single(roots);

            Assert.Equal(
                source.Id,
                root.Id);

            var distance =
                await lineageService.GetGeneratedFromDistanceAsync(
                    generated.Id,
                    source.Id);

            Assert.Equal(1, distance);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

}
