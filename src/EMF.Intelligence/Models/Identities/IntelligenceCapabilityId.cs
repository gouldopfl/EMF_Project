namespace EMF.Intelligence.Models.Identities;

public readonly record struct IntelligenceCapabilityId
{
    public IntelligenceCapabilityId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Intelligence Capability ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
