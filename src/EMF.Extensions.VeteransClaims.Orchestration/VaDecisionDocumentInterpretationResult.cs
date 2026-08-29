using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Intelligence.Models;

namespace EMF.Extensions.VeteransClaims.Orchestration;

internal sealed class VaDecisionDocumentInterpretationResult
{
    public required IntelligenceCapabilityResult<string>
        IntelligenceResult { get; init; }

    public VaDecisionDocumentInterpretation?
        Interpretation { get; init; }
}
