using EMF.Extensions.VeteransClaims.Models.Medications;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerMedication
{
    public required MedicationRecord CurrentMedication
    { get; init; }

    public MedicationHistoryEvent? EarliestDocumentedRelease
    { get; init; }
}
