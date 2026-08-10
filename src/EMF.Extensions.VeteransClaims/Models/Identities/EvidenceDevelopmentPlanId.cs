namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct EvidenceDevelopmentPlanId
{
    public EvidenceDevelopmentPlanId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Evidence Development Plan ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
