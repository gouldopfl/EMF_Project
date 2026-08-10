using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class EvidenceClassificationServiceEvent
{
    public required EvidenceClassificationId EvidenceClassificationId { get; init; }

    public required ServiceEventId ServiceEventId { get; init; }
}
