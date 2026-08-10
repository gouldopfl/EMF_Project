namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct VaDecisionId
{
    public VaDecisionId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "VA Decision ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
