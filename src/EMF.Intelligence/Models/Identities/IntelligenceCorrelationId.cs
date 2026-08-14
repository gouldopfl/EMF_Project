namespace EMF.Intelligence.Models.Identities;

public readonly record struct IntelligenceCorrelationId
{
    public IntelligenceCorrelationId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Intelligence Correlation ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
