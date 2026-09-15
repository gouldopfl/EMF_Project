namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct MedicationLedgerEntryId
{
    public MedicationLedgerEntryId(string value)
    {
        Value =
            global::EMF.Core.Models.Identities.IdentityValueValidator.Validate(
                value,
                nameof(value),
                "Medication Ledger Entry ID");
    }

    public string Value { get; }

    public override string ToString() => Value;
}
