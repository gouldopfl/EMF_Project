namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct MedicationHistoryEventId
{
    public MedicationHistoryEventId(string value)
    {
        Value =
            global::EMF.Core.Models.Identities.IdentityValueValidator.Validate(
                value,
                nameof(value),
                "Medication History Event ID");
    }

    public string Value { get; }

    public override string ToString() => Value;
}
