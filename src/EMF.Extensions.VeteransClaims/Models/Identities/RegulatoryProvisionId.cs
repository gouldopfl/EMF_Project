namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct RegulatoryProvisionId
{
    public RegulatoryProvisionId(string value)
    {
        Value =
            global::EMF.Core.Models.Identities.IdentityValueValidator.Validate(
                value,
                nameof(value),
                "Regulatory Provision ID");
    }

    public string Value { get; }

    public override string ToString() => Value;
}
