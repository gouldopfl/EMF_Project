namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct FindingId
{
    public FindingId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Finding ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
