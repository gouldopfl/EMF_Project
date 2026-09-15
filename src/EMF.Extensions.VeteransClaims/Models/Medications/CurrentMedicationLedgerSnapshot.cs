namespace EMF.Extensions.VeteransClaims.Models.Medications;

public sealed class CurrentMedicationLedgerSnapshot
{
    public required MedicationLedger Ledger { get; init; }

    public required IReadOnlyList<MedicationLedgerEntry> Entries { get; init; }
}
