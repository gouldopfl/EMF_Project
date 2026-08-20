using EMF.Core.Models;
using EMF.Intelligence.Agents;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;
using EMF.Persistence.Repositories;

namespace EMF.ConsoleApplication;

internal static class VeteransEvidenceSummaryPublisher
{
    public static async Task<Artifact> PublishAsync(
        string databasePath,
        string name,
        string promotedBy,
        string reviewedBy,
        DateTimeOffset promotedUtc,
        IntelligenceAgentResult<string> result)
    {
        var repository =
            new SqliteEvidenceRepository(databasePath);

        await repository.InitializeAsync();

        var artifact =
            new TextSummaryEvidenceArtifactFactory()
                .Create(
                    result.Output!,
                    name,
                    promotedUtc);

        await new IntelligenceEvidencePromotionService(
                repository)
            .PromoteAsync(
                new IntelligenceEvidencePromotionRequest<string>
                {
                    Artifact = artifact,
                    IntelligenceResult = result,
                    PromotedBy = promotedBy,
                    PromotedUtc = promotedUtc,
                    ReviewedBy = reviewedBy,
                    ReviewedUtc = promotedUtc
                });

        return artifact;
    }
}
