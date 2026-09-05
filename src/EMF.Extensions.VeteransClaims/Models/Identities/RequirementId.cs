namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct RequirementId
{
    public RequirementId(string value)
    {
        Value =
            global::EMF.Core.Models.Identities.IdentityValueValidator.Validate(
                value,
                nameof(value),
                "Requirement ID");
    }

    public string Value { get; }

    public override string ToString() => Value;
}
