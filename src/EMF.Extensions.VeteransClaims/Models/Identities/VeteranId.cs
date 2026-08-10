namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct VeteranId
{
    public VeteranId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Veteran ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
