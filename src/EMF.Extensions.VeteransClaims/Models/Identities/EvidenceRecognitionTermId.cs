namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct EvidenceRecognitionTermId
{
    public EvidenceRecognitionTermId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Evidence Recognition Term ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
