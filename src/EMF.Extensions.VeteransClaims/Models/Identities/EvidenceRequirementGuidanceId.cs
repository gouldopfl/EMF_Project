namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct EvidenceRequirementGuidanceId
{
    public EvidenceRequirementGuidanceId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Evidence Requirement Guidance ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
