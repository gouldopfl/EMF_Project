namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct ServiceConnectionTheoryId
{
    public ServiceConnectionTheoryId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Service Connection Theory ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
