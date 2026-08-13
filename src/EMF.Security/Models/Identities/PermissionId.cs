namespace EMF.Security.Models.Identities;

public readonly record struct PermissionId
{
    public PermissionId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Permission ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
