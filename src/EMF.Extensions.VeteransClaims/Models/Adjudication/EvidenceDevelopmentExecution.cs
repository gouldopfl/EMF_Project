using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class EvidenceDevelopmentExecution
{
    public required EvidenceDevelopmentPlanId EvidenceDevelopmentPlanId { get; init; }

    public required EvidenceGapId EvidenceGapId { get; init; }

    public required WorkflowId WorkflowId { get; init; }
}
