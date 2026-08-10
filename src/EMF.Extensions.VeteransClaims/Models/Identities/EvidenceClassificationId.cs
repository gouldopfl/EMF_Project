namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct EvidenceClassificationId
{
    public EvidenceClassificationId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Evidence Classification ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
