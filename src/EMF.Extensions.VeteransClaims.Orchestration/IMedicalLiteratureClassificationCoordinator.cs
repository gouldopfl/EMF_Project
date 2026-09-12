using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Intelligence.Models;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public interface IMedicalLiteratureClassificationCoordinator
{
    Task<MedicalLiteratureClassificationResult> ClassifyAsync(
        MedicalLiteratureSourceId sourceId,
        ArtifactId artifactId,
        IReadOnlyList<RequirementId> candidateRequirementIds,
        IntelligenceExecutionContext context,
        CancellationToken cancellationToken = default);
}
