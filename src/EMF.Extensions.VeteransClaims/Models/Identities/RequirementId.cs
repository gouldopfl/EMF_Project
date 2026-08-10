namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct RequirementId
{
    public RequirementId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Requirement ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
