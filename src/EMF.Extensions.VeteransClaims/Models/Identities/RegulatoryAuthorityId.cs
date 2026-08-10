namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct RegulatoryAuthorityId
{
    public RegulatoryAuthorityId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Regulatory Authority ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
