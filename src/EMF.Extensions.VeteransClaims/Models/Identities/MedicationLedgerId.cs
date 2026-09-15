namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct MedicationLedgerId
{
    public MedicationLedgerId(string value)
    {
        Value =
            global::EMF.Core.Models.Identities.IdentityValueValidator.Validate(
                value,
                nameof(value),
                "Medication Ledger ID");
    }

    public string Value { get; }

    public override string ToString() => Value;
}
