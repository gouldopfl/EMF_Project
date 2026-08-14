using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Intelligence.Agents;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class IntelligenceEvidencePromotionServiceTests
{
    [Fact]
    public async Task PromoteAsync_PersistsEvidenceAndLineage()
    {
        var repository = new InMemoryEvidenceRepository();
        var service =
            new IntelligenceEvidencePromotionService(repository);

        var startedUtc =
            new DateTimeOffset(
                2026, 8, 14, 18, 0, 0,
                TimeSpan.Zero);

        var correlationId =
            new IntelligenceCorrelationId("operation-001");

        var artifact = new Artifact
        {
            Id = new ArtifactId("generated-001"),
            Name = "Generated summary",
            ArtifactType = "intelligence-output"
        };

        var result = new IntelligenceAgentResult<string>
        {
            Success = true,
            Output = "Summary",
            AgentId = new AgentId("summary-agent"),
            CorrelationId = correlationId,
            StartedUtc = startedUtc,
            CompletedUtc = startedUtc.AddSeconds(2),
            SourceArtifactIds =
                [new ArtifactId("source-001")],
            CapabilityExecutions =
            [
                new IntelligenceExecutionMetadata
                {
                    CapabilityId =
                        new IntelligenceCapabilityId("summarize"),
                    ProviderId =
                        new IntelligenceProviderId("development"),
                    CorrelationId = correlationId,
                    EngineName = "deterministic",
                    EngineVersion = "1",
                    StartedUtc = startedUtc,
                    CompletedUtc = startedUtc.AddSeconds(1)
                }
            ]
        };

        await service.PromoteAsync(
            new IntelligenceEvidencePromotionRequest<string>
            {
                Artifact = artifact,
                IntelligenceResult = result,
                PromotedBy = "evidence-steward",
                PromotedUtc = startedUtc.AddSeconds(3)
            });

        Assert.NotNull(
            await repository.GetArtifactAsync(artifact.Id));

        var provenance = Assert.Single(
            await repository.GetProvenanceAsync(artifact.Id));

        Assert.Equal("EMF.Intelligence", provenance.Source);
        Assert.Equal(
            "summary-agent",
            provenance.Properties["agentId"]);

        var relationship = Assert.Single(
            await repository.GetRelationshipsAsync(artifact.Id));

        Assert.Equal(
            RelationshipTypes.GeneratedFrom,
            relationship.RelationshipType);
        Assert.Equal(
            new ArtifactId("source-001"),
            relationship.TargetArtifactId);
    }
    [Fact]
    public async Task PromoteAsync_RejectsMissingRequiredReview()
    {
        var repository = new InMemoryEvidenceRepository();
        var service =
            new IntelligenceEvidencePromotionService(repository);
        var occurredUtc =
            new DateTimeOffset(
                2026, 8, 14, 18, 0, 0,
                TimeSpan.Zero);
        var correlationId =
            new IntelligenceCorrelationId("operation-review");
        var artifact = new Artifact
        {
            Id = new ArtifactId("generated-review"),
            Name = "Generated review",
            ArtifactType = "intelligence-output"
        };

        var result = new IntelligenceAgentResult<string>
        {
            Success = true,
            Output = "Review me",
            AgentId = new AgentId("review-agent"),
            CorrelationId = correlationId,
            StartedUtc = occurredUtc,
            CompletedUtc = occurredUtc.AddSeconds(1),
            RequiresReview = true,
            SourceArtifactIds =
                [new ArtifactId("source-review")],
            CapabilityExecutions =
            [
                new IntelligenceExecutionMetadata
                {
                    CapabilityId =
                        new IntelligenceCapabilityId("summarize"),
                    ProviderId =
                        new IntelligenceProviderId("development"),
                    CorrelationId = correlationId,
                    EngineName = "deterministic",
                    StartedUtc = occurredUtc,
                    CompletedUtc = occurredUtc.AddSeconds(1)
                }
            ]
        };

        await Assert.ThrowsAsync<
            IntelligenceEvidencePromotionException>(
                () => service.PromoteAsync(
                    new IntelligenceEvidencePromotionRequest<string>
                    {
                        Artifact = artifact,
                        IntelligenceResult = result,
                        PromotedBy = "evidence-steward",
                        PromotedUtc = occurredUtc.AddSeconds(2)
                    }));

        Assert.Null(
            await repository.GetArtifactAsync(artifact.Id));
    }

}
