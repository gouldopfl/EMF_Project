namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct MedicalConditionId
{
    public MedicalConditionId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Medical Condition ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
