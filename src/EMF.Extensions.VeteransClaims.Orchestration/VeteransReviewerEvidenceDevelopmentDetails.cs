using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerEvidenceDevelopmentDetails
{
    public required EvidenceGap Gap { get; init; }

    public required EvidenceDevelopmentResult Result { get; init; }
}
