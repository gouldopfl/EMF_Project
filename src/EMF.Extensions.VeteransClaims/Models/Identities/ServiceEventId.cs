namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct ServiceEventId
{
    public ServiceEventId(string value)
    {
        Value =
            global::EMF.Core.Models.Identities.IdentityValueValidator.Validate(
                value,
                nameof(value),
                "Service Event ID");
    }

    public string Value { get; }

    public override string ToString() => Value;
}
