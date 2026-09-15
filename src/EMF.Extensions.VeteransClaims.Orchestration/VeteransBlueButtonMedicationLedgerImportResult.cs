using EMF.Extensions.VeteransClaims.Models.Medications;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransBlueButtonMedicationLedgerImportResult
{
    public required MedicationLedger Ledger { get; init; }

    public required IReadOnlyList<MedicationLedgerEntry> Entries { get; init; }

    public required bool AlreadyPersisted { get; init; }
}
