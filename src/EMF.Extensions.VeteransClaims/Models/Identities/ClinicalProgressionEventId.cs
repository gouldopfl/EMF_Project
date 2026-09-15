namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct ClinicalProgressionEventId
{
    public ClinicalProgressionEventId(string value)
    {
        Value =
            global::EMF.Core.Models.Identities.IdentityValueValidator.Validate(
                value,
                nameof(value),
                "Clinical Progression Event ID");
    }

    public string Value { get; }

    public override string ToString() => Value;
}
