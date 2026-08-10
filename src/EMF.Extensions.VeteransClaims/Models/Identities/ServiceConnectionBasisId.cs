namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct ServiceConnectionBasisId
{
    public ServiceConnectionBasisId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Service Connection Basis ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
