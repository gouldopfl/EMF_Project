namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct EvidencePackageId
{
    public EvidencePackageId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Evidence Package ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
