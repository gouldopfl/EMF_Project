namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct ClaimedConditionId
{
    public ClaimedConditionId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Claimed Condition ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
