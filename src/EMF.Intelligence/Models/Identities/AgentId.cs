namespace EMF.Intelligence.Models.Identities;

public readonly record struct AgentId
{
    public AgentId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Agent ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
