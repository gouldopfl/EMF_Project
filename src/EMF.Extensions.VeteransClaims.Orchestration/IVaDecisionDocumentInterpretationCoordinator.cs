using EMF.Core.Models.Identities;
using EMF.Intelligence.Models;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public interface IVaDecisionDocumentInterpretationCoordinator
{
    Task<VaDecisionDocumentInterpretationResult>
        InterpretAsync(
            ArtifactId artifactId,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken = default);
}
