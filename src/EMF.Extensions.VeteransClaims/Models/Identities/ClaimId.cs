namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct ClaimId
{
    public ClaimId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Claim ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
