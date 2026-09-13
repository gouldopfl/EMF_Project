namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct MedicationRecordId
{
    public MedicationRecordId(string value)
    {
        Value =
            global::EMF.Core.Models.Identities.IdentityValueValidator.Validate(
                value,
                nameof(value),
                "Medication Record ID");
    }

    public string Value { get; }

    public override string ToString() => Value;
}
