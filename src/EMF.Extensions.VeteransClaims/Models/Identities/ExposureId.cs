namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct ExposureId
{
    public ExposureId(string value)
    {
        Value =
            global::EMF.Core.Models.Identities.IdentityValueValidator.Validate(
                value,
                nameof(value),
                "Exposure ID");
    }

    public string Value { get; }

    public override string ToString() => Value;
}
