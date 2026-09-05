namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct MedicalOpinionId
{
    public MedicalOpinionId(string value)
    {
        Value =
            global::EMF.Core.Models.Identities.IdentityValueValidator.Validate(
                value,
                nameof(value),
                "Medical Opinion ID");
    }

    public string Value { get; }

    public override string ToString() => Value;
}
