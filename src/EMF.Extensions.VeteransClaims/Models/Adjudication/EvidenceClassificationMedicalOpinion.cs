using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class EvidenceClassificationMedicalOpinion
{
    public required EvidenceClassificationId EvidenceClassificationId
    {
        get;
        init;
    }

    public required MedicalOpinionId MedicalOpinionId { get; init; }
}
