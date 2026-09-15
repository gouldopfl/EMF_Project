using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Medications;

public sealed class MedicationCurrentUseReconciliation
{
    public required MedicationCurrentUseReconciliationId Id { get; init; }

    public required VeteranId VeteranId { get; init; }

    public required MedicationLedgerEntryId MedicationLedgerEntryId { get; init; }

    public required DateOnly ReconciliationDate { get; init; }

    public required string CurrentUseStatus { get; init; }

    public required string Source { get; init; }

    public string? Note { get; init; }
}
