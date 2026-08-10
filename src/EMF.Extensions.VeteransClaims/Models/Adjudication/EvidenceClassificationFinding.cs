using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class EvidenceClassificationFinding
{
    public required EvidenceClassificationId EvidenceClassificationId
    {
        get;
        init;
    }

    public required FindingId FindingId { get; init; }
}
