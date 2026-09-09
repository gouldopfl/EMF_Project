namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct MedicalLiteratureSourceId
{
    public MedicalLiteratureSourceId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Medical Literature Source ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
