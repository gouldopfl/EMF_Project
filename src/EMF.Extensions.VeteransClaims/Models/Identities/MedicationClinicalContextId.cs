namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct MedicationClinicalContextId
{
    public MedicationClinicalContextId(string value)
    {
        Value =
            global::EMF.Core.Models.Identities.IdentityValueValidator.Validate(
                value,
                nameof(value),
                "Medication Clinical Context ID");
    }

    public string Value { get; }

    public override string ToString() => Value;
}
