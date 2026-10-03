namespace EMF.Security.Models.Identities;

public readonly record struct ProtectionClassificationId
{
    [System.Text.Json.Serialization.JsonConstructor]
    public ProtectionClassificationId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Protection Classification ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
