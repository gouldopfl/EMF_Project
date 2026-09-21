using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Medications;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerMedicationProgression
{
    public ServiceConnectionBasisId? ServiceConnectionBasisId { get; init; }

    public string? ServiceConnectionBasisReviewerLabel { get; init; }

    public required string MedicationName { get; init; }

    public required IReadOnlyList<MedicationLedgerEntry> Entries { get; init; }
}
