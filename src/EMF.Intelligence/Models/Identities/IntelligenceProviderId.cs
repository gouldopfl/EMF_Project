namespace EMF.Intelligence.Models.Identities;

public readonly record struct IntelligenceProviderId
{
    public IntelligenceProviderId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Intelligence Provider ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
