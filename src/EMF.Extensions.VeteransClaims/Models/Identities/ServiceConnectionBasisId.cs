namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct ServiceConnectionBasisId
{
    public ServiceConnectionBasisId(string value)
    {
        Value =
            global::EMF.Core.Models.Identities.IdentityValueValidator.Validate(
                value,
                nameof(value),
                "Service Connection Basis ID");
    }

    public string Value { get; }

    public override string ToString() => Value;
}
