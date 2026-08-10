namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct ClaimIssueId
{
    public ClaimIssueId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Claim Issue ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
