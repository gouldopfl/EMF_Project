namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct ClaimedConditionId
{
    public ClaimedConditionId(string value)
    {
        Value =
            global::EMF.Core.Models.Identities.IdentityValueValidator.Validate(
                value,
                nameof(value),
                "Claimed Condition ID");
    }

    public string Value { get; }

    public override string ToString() => Value;
}
