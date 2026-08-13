namespace EMF.Security.Models.Identities;

public readonly record struct RoleId
{
    public RoleId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Role ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
