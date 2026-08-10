namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct EffectiveDateId
{
    public EffectiveDateId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Effective Date ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
