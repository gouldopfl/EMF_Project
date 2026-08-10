using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class EvidenceClassificationMedicalCondition
{
    public required EvidenceClassificationId EvidenceClassificationId
    {
        get;
        init;
    }

    public required MedicalConditionId MedicalConditionId { get; init; }
}
