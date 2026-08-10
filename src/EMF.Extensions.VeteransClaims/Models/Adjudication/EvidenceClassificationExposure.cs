using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class EvidenceClassificationExposure
{
    public required EvidenceClassificationId EvidenceClassificationId
    {
        get;
        init;
    }

    public required ExposureId ExposureId { get; init; }
}
