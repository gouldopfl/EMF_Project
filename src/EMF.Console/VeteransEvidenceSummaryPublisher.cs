using EMF.Core.Models;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Intelligence.Agents;
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

        return await new VeteransEvidenceSummaryPromotionService(
                new IntelligenceEvidencePromotionService(
                    repository))
            .PromoteAsync(
                name,
                promotedBy,
                reviewedBy,
                promotedUtc,
                result);
    }
}
