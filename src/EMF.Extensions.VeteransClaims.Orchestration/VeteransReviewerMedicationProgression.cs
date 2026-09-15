using EMF.Extensions.VeteransClaims.Models.Medications;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerMedicationProgression
{
    public required string MedicationName { get; init; }

    public required IReadOnlyList<MedicationLedgerEntry> Entries { get; init; }
}
