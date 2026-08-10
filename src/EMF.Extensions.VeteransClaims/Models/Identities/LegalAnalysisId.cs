namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct LegalAnalysisId
{
    public LegalAnalysisId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Legal Analysis ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
