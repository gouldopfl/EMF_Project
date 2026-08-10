namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct ServiceEventId
{
    public ServiceEventId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Service Event ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
