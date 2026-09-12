using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Intelligence.Models;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class MedicalLiteratureClassificationResult
{
    public required IntelligenceCapabilityResult<string>
        IntelligenceResult { get; init; }

    public MedicalLiteratureClassificationProposal?
        Proposal { get; init; }
}
